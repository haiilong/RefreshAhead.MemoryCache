# Getting Started

## Install

```sh
dotnet add package RefreshAhead.MemoryCache
```

Targets `net8.0` and `net10.0` (if you use `net9.0`, the `net8.0` build is picked).

## 1. Create a Cache

Derive from `RefreshAheadMemoryCache<TSnapshot>` for the common whole-snapshot refresh pattern.

```csharp
using RefreshAhead.MemoryCache;

public sealed class CountryCache()
    : RefreshAheadMemoryCache<IReadOnlyList<Country>>(Array.Empty<Country>())
{
    public override RefreshAheadSchedule Schedule =>
        RefreshAheadSchedule.DailyAt(new TimeOnly(3, 30), TimeZoneInfo.Utc);

    protected override async ValueTask<IReadOnlyList<Country>> RefreshSnapshotAsync(
        CancellationToken cancellationToken)
    {
        var countries = await LoadCountriesAsync(cancellationToken);
        return Array.AsReadOnly(countries.ToArray());
    }
}
```

The base class implements `IRefreshAheadMemoryCache<TSnapshot>` and intentionally forces the core shape:

- `Schedule` because the cache owns its refresh behavior.
- `RunImmediately` for startup warm-up control.
- `Snapshot` as the primary in-memory getter.
- `RefreshCacheAsync` as the single refresh unit of work.

Most caches only override `RefreshSnapshotAsync`. The base class calls it and publishes the returned snapshot by replacing the current snapshot reference. The read path has no lock.

Implement `IRefreshAheadMemoryCache<TSnapshot>` directly when you need custom refresh behavior, value-type snapshots, or a specialized failure policy.

The job name used in logs is inferred from the concrete cache type name, such as `CountryCache`. Generated registration emits that name as a compile-time string literal.

## 2. Register Generated Caches

```csharp
using Microsoft.Extensions.Hosting;
using RefreshAhead.MemoryCache;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddRefreshAheadMemoryCaches();

await builder.Build().RunAsync();
```

`AddRefreshAheadMemoryCaches()` is generated at compile time. It appears after the first build and registers every concrete cache type (`class` or `record`) found in the current project.

## 3. Read the Cache

Inject the concrete cache class:

```csharp
public sealed class CountryService(CountryCache cache)
{
    public IReadOnlyList<Country> GetCountries() => cache.Snapshot;
}
```

Or expose an app-specific interface:

```csharp
public interface ICountryCache
{
    IReadOnlyList<Country> Snapshot { get; }
}

public sealed class CountryCache()
    : RefreshAheadMemoryCache<IReadOnlyList<Country>>(Array.Empty<Country>()),
      ICountryCache
{
    // ...
}
```

Then register the interface as an alias to the generated singleton cache:

```csharp
builder.Services.AddSingleton<ICountryCache>(
    sp => sp.GetRequiredService<CountryCache>());

builder.Services.AddRefreshAheadMemoryCaches();
```

## Startup Warm-Up

`RunImmediately` defaults to `true`, so caches refresh as soon as the host starts.

Set it to `false` when another startup path warms the cache or when an hourly/daily cache should only run on its wall-clock schedule.

```csharp
public override bool RunImmediately => false;
```

To guarantee the cache is loaded **before** the host accepts traffic, set `WarmUpOnStart`. Host startup then blocks until the first refresh succeeds:

```csharp
public override bool WarmUpOnStart => true;
```

Bound the wait so an unavailable upstream fails startup instead of hanging:

```csharp
builder.Services.Configure<RefreshAheadMemoryCacheOptions>(options =>
    options.WarmUpTimeout = TimeSpan.FromSeconds(30));
```

See [Warm-up and health](warm-up-and-health.md) for details.

## Observing Caches

Resolve `IRefreshAheadMemoryCacheStatusProvider` to inspect refresh history (warm-up state, last success/failure, counts):

```csharp
public sealed class CacheStatusEndpoint(IRefreshAheadMemoryCacheStatusProvider statuses)
{
    public IReadOnlyList<RefreshAheadMemoryCacheStatus> GetAll() => statuses.GetStatuses();
}
```

Add a health check to surface warm-up and refresh state to orchestrators (from the companion `RefreshAhead.MemoryCache.HealthChecks` package):

```csharp
builder.Services
    .AddHealthChecks()
    .AddRefreshAheadMemoryCaches();
```

## Scoped Dependencies

Generated caches are singleton memory caches. Do not inject scoped services directly into them.

Good options:

- inject singleton-safe clients;
- inject factories such as `IDbContextFactory<TContext>`;
- use the delegate builder when the refresh must create a fresh DI scope every time.

```csharp
builder.Services.AddRefreshAheadMemoryCaches(refresh => refresh
    .Every<IScopedRefreshService>(
        TimeSpan.FromMinutes(5),
        static (service, ct) => service.RefreshCacheAsync(ct)));
```

The generic delegate builder resolves the target service from a fresh scope on every refresh.
