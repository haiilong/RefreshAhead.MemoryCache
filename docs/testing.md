# Testing

The scheduler uses `TimeProvider`, so timing behavior can be tested with `FakeTimeProvider`.

```csharp
var time = new FakeTimeProvider(
    new DateTimeOffset(2026, 5, 26, 10, 0, 0, TimeSpan.Zero));
services.AddSingleton<TimeProvider>(time);
```

The package tests cover:

- argument validation for invalid schedule values;
- fixed-delay scheduling from completion time;
- fixed-rate skipped ticks;
- fixed-rate catch-up after a large missed window;
- hourly and daily wall-clock scheduling;
- immediate refresh on hosted-service start;
- delayed first run with `runImmediately: false`;
- non-overlap while a refresh is still running;
- continuation after refresh failure;
- fresh DI scope resolution for each delegate-builder refresh registration;
- source-generated registration of `IRefreshAheadMemoryCache<TSnapshot>` implementations.

## Consumer tests

Most application tests should not test the scheduler. Instead, test your cache refresh function directly and register the scheduler only in host-level tests.

## Testing a Generated Cache

Generated caches are plain singleton services after registration:

```csharp
var services = new ServiceCollection();
services.AddLogging();
services.AddRefreshAheadMemoryCaches();

await using var provider = services.BuildServiceProvider();

var cache = provider.GetRequiredService<MyCache>();
await cache.RefreshCacheAsync(CancellationToken.None);

cache.Snapshot.Should().NotBeEmpty();
```

For timing tests, inject `FakeTimeProvider` before calling the generated registration method:

```csharp
var time = new FakeTimeProvider(
    new DateTimeOffset(2026, 5, 26, 10, 0, 0, TimeSpan.Zero));

services.AddSingleton<TimeProvider>(time);
services.AddRefreshAheadMemoryCaches();
```
