# Troubleshooting

## "`AddRefreshAheadMemoryCaches` is not recognized"

The no-argument `AddRefreshAheadMemoryCaches()` method is emitted by the source generator. Run a build once:

```sh
dotnet build
```

Also check:

- the project references the NuGet package, not only a transitive library;
- the cache class is in the same compilation as the app calling the generated method;
- the cache class is concrete, non-generic, and implements `IRefreshAheadMemoryCache<TSnapshot>`;
- the file has `using RefreshAhead.MemoryCache;`.

If the current project has no concrete refresh-ahead cache classes, the generator does not emit the no-argument method. Use the delegate-builder overload instead.

When developing this repository with `ProjectReference`, samples and tests must reference the source-generator project as an analyzer. NuGet consumers receive the analyzer automatically.

## My cache was not auto-registered

The generator only registers concrete, non-abstract, non-generic classes that implement `IRefreshAheadMemoryCache<TSnapshot>`.

It does not register:

- abstract base caches;
- open generic cache types;
- classes that only expose a `RefreshCacheAsync` method but do not implement the interface;
- cache classes from a referenced assembly.

Use the delegate builder for manual registration when the source-generator convention does not fit.

If your caches live in a separate class library, add a public wrapper in that library:

```csharp
public static class MyCacheRegistration
{
    public static IServiceCollection AddMyRefreshAheadCaches(
        this IServiceCollection services) =>
        services.AddRefreshAheadMemoryCaches();
}
```

The wrapper can call the generated internal method because it is compiled into the same class library as the caches. Your app then calls `AddMyRefreshAheadCaches()`.

## My cache refresh runs in every pod

That is expected. This library is in-process. Each app instance maintains its own in-memory cache.

If a job must run globally once across replicas, use `SingletonJob`.

## Refresh takes longer than the interval

Refreshes never overlap.

- `Every` waits until the refresh completes, then waits the interval.
- `FixedRate` skips missed ticks and schedules the next future tick.

## My refresh failed and the old snapshot is still being served

That is expected. The scheduler logs refresh exceptions and keeps the loop alive, but it does not mutate your cache snapshot.

If stale data is unsafe for your domain, reset the snapshot inside `RefreshCacheAsync` and rethrow so the scheduler still records the failed refresh. See [Failure policy](failure-policy.md).

## I do not see per-refresh logs

Per-refresh start and completion logs are `Debug`. Failures are `Error`. Raise the category for troubleshooting:

```json
{
  "Logging": {
    "LogLevel": {
      "RefreshAhead.MemoryCache": "Debug"
    }
  }
}
```

## My first refresh runs immediately

`runImmediately` defaults to `true`. Set it to `false` for hourly/daily jobs or when another startup path warms the cache.

For the primary interface API:

```csharp
public override bool RunImmediately => false;
```

For the delegate builder:

```csharp
refresh.Every<IMyCache>(
    TimeSpan.FromMinutes(5),
    static (cache, ct) => cache.RefreshCacheAsync(ct),
    runImmediately: false);
```

## I get an error resolving a scoped service

Generated caches are singleton memory caches. Do not inject scoped services directly into them.

Use one of these approaches:

- inject a singleton-safe client;
- inject a factory such as `IDbContextFactory<TContext>`;
- use the delegate builder, which resolves generic delegate targets from a fresh scope on every refresh.
