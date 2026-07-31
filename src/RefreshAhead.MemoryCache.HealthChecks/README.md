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
| Unhealthy (or the configured failure status) | a refresh loop faulted, or a cache is not warm after it was due to run. |
| Degraded | a warm cache's latest attempt failed, or its last success exceeds an optional maximum age. |
| Healthy | caches are operating normally, including delayed-first-run caches that are not due yet. |

Per-cache running/faulted state, duration, next run, timestamps, counters, and errors are included in the health-check data.

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

Scope a check to critical caches:

```csharp
builder.Services.AddHealthChecks().AddRefreshAheadMemoryCaches(
    cacheNames: ["PriceCache", "ReferenceDataCache"],
    name: "critical-caches",
    tags: ["ready"]);
```

Or configure a maximum age for the last successful refresh:

```csharp
builder.Services.AddHealthChecks().AddRefreshAheadMemoryCaches(
    maxAge: TimeSpan.FromMinutes(10));
```

See the [main package documentation](https://github.com/haiilong/RefreshAhead.MemoryCache/blob/main/docs/warm-up-and-health.md) for details.

## License

MIT
