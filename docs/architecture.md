# Architecture

`RefreshAhead.MemoryCache` has two layers:

1. A primary cache interface plus source generator.
2. A lower-level delegate builder.

Both layers feed the same hosted-service runtime.

## Primary Flow

1. Your project defines one or more concrete caches, usually by deriving from `RefreshAheadMemoryCache<TSnapshot>`.
2. The source generator finds those classes at compile time.
3. The generator emits an internal no-argument `AddRefreshAheadMemoryCaches()` extension into your assembly.
4. That generated method registers each cache as a singleton and adds one refresh job for it.
5. `RefreshAheadMemoryCacheService` starts one async loop per job.
6. Each loop awaits the refresh before scheduling the next run.
7. Exceptions are logged and the loop continues.
8. Host shutdown cancels delays and refresh methods that observe cancellation.

The cache class is the unit of work. Its `Schedule`, `RunImmediately`, `Snapshot`, and refresh behavior live together because they describe one memory cache.

For the common case, `RefreshAheadMemoryCache<TSnapshot>` calls your `RefreshSnapshotAsync` override and publishes the returned snapshot by replacing the current snapshot reference. The read path does not lock.

Job names used in logs are inferred from the concrete cache type name. The source generator passes that name as a string literal, and the delegate builder still supports an explicit `name` parameter for manual registrations.

Refresh failure policy also belongs to the cache. The runtime does not reset snapshots when a refresh throws; it logs the failure and continues. See [Failure policy](failure-policy.md) for last-good and reset-after-failure patterns.

## Generated Registration

The generator emits code shaped like this:

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

This gives the convenience of auto-registration without runtime scanning.

## Cache Lifetime

Generated caches are registered as singletons.

That is intentional. A refresh-ahead memory cache is process-local state. If each refresh created a new cache instance, callers would not read the refreshed snapshot.

Because caches are singleton, constructor dependencies must be singleton-safe. If refresh work needs scoped services, inject factories or use the delegate builder.

## Non-Overlap

There is no queue. A job loop awaits the current refresh before scheduling the next run. That gives the same practical protection as Quartz's `DisallowConcurrentExecution` for a single in-process cache job.

## Delegate Builder

The delegate builder is the manual layer:

```csharp
builder.Services.AddRefreshAheadMemoryCaches(refresh => refresh
    .Every<IPriceCache>(
        TimeSpan.FromSeconds(1),
        static (cache, ct) => cache.RefreshCacheAsync(ct)));
```

Generic delegate registrations resolve the target service from a fresh DI scope on every run. This makes the delegate builder useful for:

- existing caches that do not implement the package interface;
- refresh work that needs scoped services;
- dynamic registration loops;
- multiple refresh delegates for one service;
- tests and migration paths.

The low-level `Add(name, schedule, refreshAsync)` overload receives the root `IServiceProvider` directly. Use it only when you want to manage scope creation yourself.

## What the Library Does Not Do

- No persistence.
- No retries beyond the next scheduled run.
- No dashboard.
- No cross-pod singleton election.
- No distributed locks.

Use `SingletonJob` when a job must run in exactly one replica.
