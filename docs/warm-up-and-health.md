# Warm-Up, Status, and Health

A refresh-ahead memory cache exists so data is ready before request handlers need it. This page covers three features that make that promise observable and enforceable:

- **Warm-up** — block host startup until a cache has loaded once.
- **Status** — inspect each cache's refresh history at runtime.
- **Health check** — surface warm-up and refresh state to orchestrators.

## Startup Warm-Up

By default the host starts immediately and refreshes run in the background. There is a short window where `Snapshot` returns the initial value supplied at construction (often empty) while traffic is already being served.

Set `WarmUpOnStart` to close that window:

```csharp
public sealed class PriceCache()
    : RefreshAheadMemoryCache<IReadOnlyDictionary<string, decimal>>(
        FrozenDictionary<string, decimal>.Empty)
{
    public override RefreshAheadSchedule Schedule =>
        RefreshAheadSchedule.FixedDelay(TimeSpan.FromSeconds(1));

    public override bool WarmUpOnStart => true;

    protected override async ValueTask<IReadOnlyDictionary<string, decimal>> RefreshSnapshotAsync(
        CancellationToken cancellationToken) => /* ... */;
}
```

When `WarmUpOnStart` is `true`:

- the first refresh runs eagerly on startup, **even if `RunImmediately` is `false`**;
- the host is not reported as started until that first refresh **succeeds**;
- if the first refresh fails, the loop keeps retrying on its normal schedule and startup keeps waiting for a success.

This is implemented through `IHostedLifecycleService`: warm-up is awaited after every refresh loop has started, so multiple warm-up caches load concurrently.

### Bounding warm-up with a timeout

Waiting for a success forever is dangerous when an upstream dependency is down. Set a timeout so startup fails fast instead of hanging:

```csharp
builder.Services.Configure<RefreshAheadMemoryCacheOptions>(options =>
    options.WarmUpTimeout = TimeSpan.FromSeconds(30));
```

- When `WarmUpTimeout` is set and warm-up does not complete in time, startup throws a `TimeoutException`.
- When `WarmUpTimeout` is `null` (the default), startup waits indefinitely, bounded only by the host's own startup cancellation token.

Set a timeout in production unless you are certain the upstream is always available at startup.

### Warm-up with the delegate builder

Every delegate-builder overload accepts `warmUpOnStart`:

```csharp
builder.Services.AddRefreshAheadMemoryCaches(refresh => refresh
    .Every<IPriceCache>(
        TimeSpan.FromSeconds(1),
        static (cache, ct) => cache.RefreshCacheAsync(ct),
        warmUpOnStart: true));
```

## Status

Resolve `IRefreshAheadMemoryCacheStatusProvider` to inspect each cache's refresh history. It is registered as a singleton whenever you call `AddRefreshAheadMemoryCaches(...)`.

```csharp
public sealed class CacheStatusEndpoint(IRefreshAheadMemoryCacheStatusProvider statuses)
{
    public IReadOnlyList<RefreshAheadMemoryCacheStatus> GetAll() => statuses.GetStatuses();

    public bool IsPriceCacheWarm() =>
        statuses.TryGetStatus("PriceCache", out var status) && status.IsWarm;
}
```

`RefreshAheadMemoryCacheStatus` exposes:

| Member | Meaning |
|---|---|
| `Name` | The job name (concrete cache type name by default). |
| `IsWarm` | `true` once at least one refresh has succeeded. While `false`, callers still read the initial snapshot. |
| `LastRefreshStartedAt` | UTC start time of the most recent attempt. |
| `LastSuccessAt` / `LastFailureAt` | UTC time of the most recent success / failure. |
| `LastError` | Message of the most recent failure, or `null` if the last refresh succeeded. Full exceptions are logged at `Error`. |
| `RefreshCount` / `SuccessCount` / `FailureCount` | Lifetime counters. |
| `ConsecutiveFailures` | Failures since the last success; reset to zero on success. |

Status is tracked for both interface caches and delegate-builder jobs.

## Health Check

The health check ships in a separate package, `RefreshAhead.MemoryCache.HealthChecks`, so the core library does not force a health-checks dependency on consumers that do not need it:

```sh
dotnet add package RefreshAhead.MemoryCache.HealthChecks
```

```csharp
builder.Services
    .AddHealthChecks()
    .AddRefreshAheadMemoryCaches();
```

The check reports:

| Status | When |
|---|---|
| Unhealthy (or the configured failure status) | any cache has never refreshed successfully and is still serving its initial snapshot. |
| Degraded | every cache is warm, but at least one is serving stale data after a failed refresh (`ConsecutiveFailures > 0`). |
| Healthy | every cache is warm and its most recent refresh succeeded. |

Per-cache state (`isWarm`, `lastSuccessAt`, `lastFailureAt`, `consecutiveFailures`, `lastError`) is included in the health-check `data` for diagnostics.

You can customize the registration:

```csharp
builder.Services
    .AddHealthChecks()
    .AddRefreshAheadMemoryCaches(
        name: "caches",
        failureStatus: HealthStatus.Degraded,
        tags: ["ready"]);
```

Tagging with `ready` and mapping a readiness endpoint that filters on that tag pairs naturally with `WarmUpOnStart`: the pod reports not-ready until its caches are warm.

## How They Fit Together

- `WarmUpOnStart` makes the cache ready before the host accepts traffic.
- The status provider lets you observe refresh health from application code.
- The health check exposes that same state to Kubernetes, load balancers, or any orchestrator.

All three are opt-in and independent. None of them change the failure policy: the library still never mutates your snapshot on failure. See [Failure policy](failure-policy.md).
