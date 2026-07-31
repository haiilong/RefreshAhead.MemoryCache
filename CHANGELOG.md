# Changelog

## 1.1.1 - 2026-07-31

Hardens scheduling, registration, warm-up, and health reporting after the 1.1.0 release.

### Reliability

- Observe unexpected scheduling/loop exceptions immediately: mark the loop faulted, fail any pending warm-up gate, log at `Critical`, and request host shutdown instead of leaving one cache silently frozen behind `Task.WhenAll`.
- Chunk waits longer than the platform timer limit, and clamp fixed-delay/fixed-rate date arithmetic at `DateTimeOffset.MaxValue` instead of throwing on overflow.
- Preserve both occurrences of an ambiguous `HourlyAt` time during a daylight-saving fall-back.
- Validate that `WarmUpTimeout`, when set, is greater than zero. Warm-up wait and timeout diagnostics now name the blocking cache jobs.
- Avoid capturing a consumer `SynchronizationContext` from library awaits; CA2007 is enforced for shipping source.

### Registration

- Make repeated registration of the same concrete cache idempotent, preventing duplicate refresh loops.
- Reject duplicate job names during registration and again when runtime status entries are created.
- Register cache interfaces by keyed job name. When multiple caches share one snapshot type, unkeyed `IRefreshAheadMemoryCache<TSnapshot>` resolution now fails with guidance instead of silently returning whichever cache was registered last.

### Status and health

- Add `WarmUpOnStart`, `IsRefreshing`, `IsRefreshLoopFaulted`, `LastRefreshDuration`, `NextScheduledRunAt`, and `RefreshLoopError` to `RefreshAheadMemoryCacheStatus`.
- Treat a delayed first-run cache as healthy until its first scheduled run is due, while reporting faulted loops through the configured failure status.
- Add health-check overloads for cache-name scoping and a maximum age for the last successful refresh.

### Packaging and compatibility

- Enable NuGet package validation against the 1.1.0 baseline for both packages.
- Correct the 1.1.0 compatibility note: adding optional `warmUpOnStart` parameters preserved source compatibility, but changed interface method signatures and was not binary compatible for already-compiled 1.0.0 callers. Future options will be introduced with additive overloads rather than by widening existing interface methods.

## 1.1.0 - 2026-06-15

Adds startup warm-up, refresh observability, and a health check. Existing source continues to compile unchanged.

### Startup warm-up

- Add `WarmUpOnStart` to `IRefreshAheadMemoryCache<TSnapshot>` (default `false`) and to every delegate-builder overload. When `true`, the first refresh runs eagerly on startup (regardless of `RunImmediately`) and host startup blocks until that refresh succeeds.
- Implement `IHostedLifecycleService` so warm-up gating happens after all refresh loops have started and before the host is reported as started.
- Add `RefreshAheadMemoryCacheOptions.WarmUpTimeout`. When set, startup fails with a `TimeoutException` if warm-up does not complete in time; when `null` (default), startup waits indefinitely, bounded only by the host startup cancellation token. Configure with `services.Configure<RefreshAheadMemoryCacheOptions>(...)`.

### Refresh observability

- Add `IRefreshAheadMemoryCacheStatusProvider` (registered as a singleton) exposing `RefreshAheadMemoryCacheStatus` per cache job: `IsWarm`, last refresh start/success/failure times, last error message, and refresh/success/failure/consecutive-failure counts.
- The hosted service records start, success, and failure for every refresh, covering both interface caches and delegate-builder jobs.

### Health check (new companion package: `RefreshAhead.MemoryCache.HealthChecks` 1.1.0)

- Add `RefreshAheadMemoryCacheHealthCheck` and `IHealthChecksBuilder.AddRefreshAheadMemoryCaches(...)` in a separate package so the core library stays lightweight. Reports Unhealthy while any cache is not yet warm, Degraded when a warm cache is serving stale data after a failed refresh, and Healthy otherwise. Per-cache state is included in the health-check data.
- The core package adds only `Microsoft.Extensions.Options` (required by warm-up). The `Microsoft.Extensions.Diagnostics.HealthChecks` dependency lives in the companion package.
- Add the public `IServiceCollection.AddRefreshAheadMemoryCacheStatusTracking()` extension so the companion package (and consumers) can register the status provider independently.

### Source generator

- Discover `record` (record class) cache types in addition to `class` types. Record structs and other value types are not registered.

### 1.0.0 compatibility

- `WarmUpOnStart` on the cache interface is default-implemented and source compatible. The optional builder parameters are source compatible but changed the interface method signatures, so already-compiled libraries that call the 1.0.0 builder interface must be recompiled against 1.1.x.

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
