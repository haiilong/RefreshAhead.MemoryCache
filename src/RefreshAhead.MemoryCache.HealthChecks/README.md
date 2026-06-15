# RefreshAhead.MemoryCache.HealthChecks

A health check for [`RefreshAhead.MemoryCache`](https://www.nuget.org/packages/RefreshAhead.MemoryCache/). It reports the warm-up and refresh state of every registered refresh-ahead memory cache through `Microsoft.Extensions.Diagnostics.HealthChecks`.

This is a separate package so the core `RefreshAhead.MemoryCache` library stays lightweight and does not force a health-checks dependency on consumers that do not need it.

## Install

```sh
dotnet add package RefreshAhead.MemoryCache.HealthChecks
```

Targets `net8.0` and `net10.0`. Depends on `RefreshAhead.MemoryCache`.

## Usage

```csharp
builder.Services
    .AddHealthChecks()
    .AddRefreshAheadMemoryCaches();
```

It reports:

| Status | When |
|---|---|
| Unhealthy (or the configured failure status) | any cache has never refreshed successfully and is still serving its initial snapshot. |
| Degraded | every cache is warm, but at least one is serving stale data after a failed refresh. |
| Healthy | every cache is warm and its most recent refresh succeeded. |

Per-cache state (`isWarm`, `lastSuccessAt`, `lastFailureAt`, `consecutiveFailures`, `lastError`) is included in the health-check data.

Customize the registration:

```csharp
builder.Services
    .AddHealthChecks()
    .AddRefreshAheadMemoryCaches(
        name: "caches",
        failureStatus: HealthStatus.Degraded,
        tags: ["ready"]);
```

Tagging with `ready` and mapping a readiness endpoint filtered on that tag pairs naturally with `WarmUpOnStart`: the pod reports not-ready until its caches are warm.

See the [main package documentation](https://github.com/haiilong/RefreshAhead.MemoryCache/blob/main/docs/warm-up-and-health.md) for details.

## License

MIT
