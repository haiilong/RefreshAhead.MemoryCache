# Changelog

## 1.0.0 - 2026-05-26

First stable release.

### Primary cache API

- Add `IRefreshAheadMemoryCache<TSnapshot>` as the primary cache-owned schedule API: the cache declares its `Schedule`, `RunImmediately`, current `Snapshot`, and `RefreshCacheAsync`.
- Add `RefreshAheadMemoryCache<TSnapshot>` base class for lock-free whole-snapshot publishing via `RefreshSnapshotAsync`. The read path uses a volatile reference swap and never blocks.
- Make `PublishSnapshot` virtual so derived caches can count publications, emit domain metrics, or apply a reset/fallback policy without reimplementing the normal refresh flow.
- Infer cache job names from the concrete cache type name instead of requiring a `CacheName` property.

### Source-generated registration

- Add bundled Roslyn source generator that discovers concrete `IRefreshAheadMemoryCache<TSnapshot>` implementations at compile time and emits an internal, AOT-safe `AddRefreshAheadMemoryCaches()` extension into the consuming assembly.
- Register generated caches as singleton process-local memory caches and expose them through `IRefreshAheadMemoryCache<TSnapshot>` for callers that prefer the interface.
- Mark the package `IsAotCompatible=true` and `IsTrimmable=true`; the generated registration carries the constructor-preservation annotation required by `Microsoft.Extensions.DependencyInjection`.

### Delegate builder

- Keep the fluent `AddRefreshAheadMemoryCaches(refresh => ...)` builder as the escape hatch for scoped DI, dynamic registration, migration, and tests.
- Add cancellation-aware `Task` overloads so `Task RefreshAsync(CancellationToken)` methods can be registered without an async wrapper.
- Resolve generic delegate registrations from a fresh DI scope on every refresh.

### Scheduling

- Add hosted service that runs one non-overlapping refresh loop per registered cache/job.
- Add `FixedDelay`, `FixedRate`, `HourlyAt`, and `DailyAt` schedules.
- Compute fixed-rate catch-up in one calculation so large missed windows do not iterate per tick.
- Advance daylight-saving invalid local times minute by minute until a valid local time is reached.
- Ensure delayed first-run cancellation follows the normal job stop path.
- Drive timing through `TimeProvider` so `FakeTimeProvider` can be used in tests.

### Logging

- Log job start/stop at `Information`, per-refresh start/success at `Debug`, and refresh exceptions at `Error` via the `LoggerMessage` source generator.

### Targeting

- Target `net8.0` and `net10.0`. Consumers on `net9.0` pick the `net8.0` build.

### Tests, samples, and docs

- Add tests for argument validation, fixed-delay scheduling from completion time, fixed-rate skipped ticks, large fixed-rate catch-up windows, hourly/daily wall-clock scheduling, immediate refresh on host start, delayed first run with `RunImmediately = false`, non-overlap while a refresh is running, continuation after refresh failure, fresh DI scope resolution per refresh, and source-generated registration of concrete cache classes.
- Add a worker sample with two source-generated caches and Docker Compose support.
- Add documentation: getting started, scheduling, architecture, AOT and source generation, failure policy, testing, and troubleshooting.
