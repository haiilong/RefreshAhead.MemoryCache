# RefreshAhead.MemoryCache

Lightweight refresh-ahead scheduling for in-process .NET memory caches. Cache classes declare their own schedule, expose their current snapshot, and are auto-registered by a bundled source generator.

[![NuGet](https://img.shields.io/nuget/v/RefreshAhead.MemoryCache.svg)](https://www.nuget.org/packages/RefreshAhead.MemoryCache/)
[![Build](https://github.com/haiilong/RefreshAhead.MemoryCache/actions/workflows/ci.yml/badge.svg)](https://github.com/haiilong/RefreshAhead.MemoryCache/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

## Why This Exists

Many services keep small local snapshots: prices, permissions, reference data, feature tables, routing metadata, tenant settings. Those caches are not durable jobs. They are in-memory state owned by the current process, and they need to be refreshed before request handlers need them.

`RefreshAhead.MemoryCache` is for that shape:

- the cache itself is the unit of work;
- the cache declares when it refreshes;
- callers read the cache through a primary `Snapshot` getter;
- refreshes run inside a hosted service;
- refreshes never overlap for the same cache;
- failures are logged and the next scheduled refresh still happens;
- registration is generated at compile time, with no runtime assembly scanning.

Use [`SingletonJob`](https://github.com/haiilong/SingletonJob) when exactly one replica in a fleet should run a global job. Use this package when every app instance should maintain its own local memory cache.

## Install

```sh
dotnet add package RefreshAhead.MemoryCache
```

Targets `net8.0` and `net10.0` (if you use `net9.0`, the `net8.0` build is picked).

## Quickstart

Create a cache class:

```csharp
using System.Collections.Frozen;
using RefreshAhead.MemoryCache;

public sealed class PriceCache()
    : RefreshAheadMemoryCache<IReadOnlyDictionary<string, decimal>>(
        FrozenDictionary<string, decimal>.Empty)
{
    public override RefreshAheadSchedule Schedule =>
        RefreshAheadSchedule.FixedDelay(TimeSpan.FromSeconds(1));

    protected override async ValueTask<IReadOnlyDictionary<string, decimal>> RefreshSnapshotAsync(
        CancellationToken cancellationToken)
    {
        var next = await LoadPricesAsync(cancellationToken);
        return next.ToFrozenDictionary();
    }
}
```

Register generated caches:

```csharp
using Microsoft.Extensions.Hosting;
using RefreshAhead.MemoryCache;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddRefreshAheadMemoryCaches();

await builder.Build().RunAsync();
```

`AddRefreshAheadMemoryCaches()` is emitted by the bundled Roslyn source generator. It discovers every concrete `IRefreshAheadMemoryCache<TSnapshot>` implementation in your compilation and registers it without reflection.

> First build required: because the method is generated, a fresh project may show `CS1061` in the IDE until you run `dotnet build` once.

## Primary API

```csharp
public interface IRefreshAheadMemoryCache<out TSnapshot>
{
    RefreshAheadSchedule Schedule { get; }

    bool RunImmediately => true;

    TSnapshot Snapshot { get; }

    ValueTask RefreshCacheAsync(CancellationToken cancellationToken);
}
```

Most caches can derive from `RefreshAheadMemoryCache<TSnapshot>` instead of implementing the interface directly:

```csharp
public abstract class RefreshAheadMemoryCache<TSnapshot>
    : IRefreshAheadMemoryCache<TSnapshot>
    where TSnapshot : class
{
    public TSnapshot Snapshot { get; }

    public virtual ValueTask RefreshCacheAsync(CancellationToken cancellationToken);

    protected abstract ValueTask<TSnapshot> RefreshSnapshotAsync(
        CancellationToken cancellationToken);

    protected virtual void PublishSnapshot(TSnapshot snapshot);
}
```

The base class calls `RefreshSnapshotAsync`, then publishes the returned snapshot by replacing the current snapshot reference. The read path has no lock; callers either see the previous complete snapshot or the next complete snapshot. `PublishSnapshot` is virtual so advanced caches can count publications, emit domain metrics, or apply a reset/fallback policy without reimplementing the normal refresh flow.

Implement the interface directly when you need custom refresh behavior, value-type snapshots, or a specialized failure policy.

Cache names used in logs are inferred from the concrete cache type name, such as `PriceCache`. The generated registration passes that name as a compile-time string literal. Manual builder registration still accepts an explicit `name` when you want a different log label.

### Why the Schedule Lives on the Cache

For a refresh-ahead memory cache, the cache is the unit of work. Its data shape, refresh behavior, and read API are tightly related. Keeping `Schedule` on the cache class makes the class self-describing:

- reading the cache class tells you what it stores and when it refreshes;
- tests can construct the cache and assert its schedule directly;
- generated registration can wire the job without a separate composition-time map;
- the package feels closer to `SingletonJob`, where the job class declares its own execution behavior.

Schedules can still use DI configuration. Inject options into the cache constructor, compute the schedule once, and return it from the property.

### Why There Is a Snapshot Getter

The package forces one primary read surface through `Snapshot` because a memory cache should expose its current in-memory value. The snapshot type is generic so you can choose the shape:

```csharp
IRefreshAheadMemoryCache<IReadOnlyDictionary<string, decimal>>
IRefreshAheadMemoryCache<IReadOnlyList<Country>>
IRefreshAheadMemoryCache<TenantSettingsSnapshot>
```

You can still add more granular getters on your class:

```csharp
public bool TryGetPrice(string symbol, out decimal price) =>
    Snapshot.TryGetValue(symbol, out price);
```

If callers prefer an app-specific interface, register an alias:

```csharp
builder.Services.AddSingleton<IPriceCache>(
    sp => sp.GetRequiredService<PriceCache>());

builder.Services.AddRefreshAheadMemoryCaches();
```

The generated registration owns `PriceCache` as the singleton cache instance; your interface points at the same object.

## Source-Generated Registration

At compile time, the generator emits an internal extension method into your app, roughly like this:

```csharp
internal static IServiceCollection AddRefreshAheadMemoryCaches(
    this IServiceCollection services)
{
    return services.AddRefreshAheadMemoryCaches(refresh =>
    {
        refresh.Add<PriceCache, IReadOnlyDictionary<string, decimal>>(name: "PriceCache");
        refresh.Add<ReferenceDataCache, IReadOnlyList<string>>(name: "ReferenceDataCache");
    });
}
```

There is no `Assembly.GetTypes()`, no runtime scanning, and no dynamic activation path in the generated discovery step.

Generated cache registrations are singleton registrations. That is intentional: this package is for in-memory process-local state. Keep constructor dependencies singleton-safe. If refresh work needs scoped services like EF Core `DbContext`, inject a factory (`IDbContextFactory<TContext>`) or use the delegate builder described below.

## Delegate Builder

The delegate builder is still provided on purpose:

```csharp
builder.Services.AddRefreshAheadMemoryCaches(refresh => refresh
    .Every<IPriceCache>(
        TimeSpan.FromSeconds(1),
        static (cache, ct) => cache.RefreshCacheAsync(ct)));
```

Use it when:

- you are migrating existing caches that cannot implement the package interface yet;
- refresh logic needs a fresh DI scope on every run;
- a cache is registered behind an app-specific interface only;
- schedules come from a dynamic registration loop;
- you want one service to host several distinct refresh delegates;
- tests need a tiny hand-built registration without invoking the generator.

For new caches, prefer `IRefreshAheadMemoryCache<TSnapshot>` plus generated registration. The delegate builder is the escape hatch, not the main path.

## Schedules

### Every / FixedDelay

Run, complete, then wait. This is the safest default because slow refreshes naturally push out the next run.

```csharp
public RefreshAheadSchedule Schedule =>
    RefreshAheadSchedule.FixedDelay(TimeSpan.FromSeconds(30));
```

### FixedRate

Aim for a fixed cadence. If refresh takes too long or the process is paused, missed ticks are skipped and the next future tick is scheduled.

```csharp
public RefreshAheadSchedule Schedule =>
    RefreshAheadSchedule.FixedRate(TimeSpan.FromSeconds(5));
```

### HourlyAt

Run once per hour at an offset from the top of the hour.

```csharp
public RefreshAheadSchedule Schedule =>
    RefreshAheadSchedule.HourlyAt(TimeSpan.FromMinutes(10), TimeZoneInfo.Utc);
```

### DailyAt

Run once per day at a local time.

```csharp
public RefreshAheadSchedule Schedule =>
    RefreshAheadSchedule.DailyAt(new TimeOnly(3, 30), TimeZoneInfo.Utc);
```

For daylight-saving gaps, invalid local times are advanced minute by minute until a valid local time is reached.

## Runtime Semantics

- One loop is created per registered refresh cache/job.
- A cache never overlaps itself.
- Exceptions are logged and the loop continues on the next scheduled run.
- `FixedDelay` schedules from completion time.
- `FixedRate` skips missed ticks and schedules the next future tick.
- `RunImmediately` defaults to `true`.
- Host shutdown cancels pending delays and refresh delegates that observe cancellation.
- Generated cache registrations are singleton and process-local.
- The library does not coordinate across pods.

## Refresh Failure Policy

The library does not reset or mutate your snapshot when `RefreshCacheAsync` throws. That policy belongs inside the cache because only the cache knows whether stale data, empty data, partial data, or fallback data is safest.

Recommended default: build the next snapshot first, then atomically swap it only after the refresh succeeds. A transient failure then keeps the last good snapshot.

```csharp
protected override async ValueTask<IReadOnlyDictionary<string, decimal>> RefreshSnapshotAsync(
    CancellationToken cancellationToken)
{
    var next = await LoadSnapshotAsync(cancellationToken);
    return next.ToFrozenDictionary();
}
```

If your domain needs a different policy, apply it inside the cache and rethrow so the scheduler still logs/counts the failed refresh:

```csharp
public async ValueTask RefreshCacheAsync(CancellationToken cancellationToken)
{
    try
    {
        await base.RefreshCacheAsync(cancellationToken);
        _consecutiveFailures = 0;
    }
    catch
    {
        _consecutiveFailures++;

        if (_consecutiveFailures >= 3)
        {
            PublishSnapshot(FrozenDictionary<string, decimal>.Empty);
        }

        throw;
    }
}
```

See [Failure policy](docs/failure-policy.md) for more patterns.

## Logging

| Event | Level |
|---|---|
| Job start / stop | Information |
| Per-refresh start / success | Debug |
| Refresh exception | Error |

Per-refresh logs are `Debug` so high-frequency refresh loops do not flood `Information` logs.

## AOT / Trim

The package declares `IsAotCompatible=true` and `IsTrimmable=true`. The source generator emits direct generic registrations for discovered caches. The generic registration method carries constructor-preservation annotations required by `Microsoft.Extensions.DependencyInjection`, so the package remains trim-clean.

See [AOT and source generation](docs/aot.md) for details.

## Documentation

| Document | Contents |
|---|---|
| [Getting started](docs/getting-started.md) | Primary API, generated registration, app-specific interfaces |
| [Scheduling](docs/scheduling.md) | `FixedDelay`, `FixedRate`, `HourlyAt`, `DailyAt` behavior |
| [Architecture](docs/architecture.md) | Hosted service loops, generated registration, delegate builder |
| [AOT and source generation](docs/aot.md) | How the generator works and how to troubleshoot generated output |
| [Failure policy](docs/failure-policy.md) | Last-good, reset-on-failure, and reset-after-N-failures patterns |
| [Testing](docs/testing.md) | `TimeProvider`, generator tests, behavior covered by package tests |
| [Troubleshooting](docs/troubleshooting.md) | Common surprises and log settings |

## Try It Locally

See [samples](samples/README.md) for a worker sample with two generated refresh-ahead caches.

```sh
cd samples
docker compose up --build
```

## License

MIT
