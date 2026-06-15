# RefreshAhead.MemoryCache samples

One worker process with two source-generated refresh-ahead memory caches.

## Caches in this sample

| Cache | Schedule | Notes |
|---|---|---|
| `PriceCache` | fixed delay every 1 second | run, then wait; `WarmUpOnStart = true` so startup blocks until the first load |
| `ReferenceDataCache` | hourly at 10 seconds past the hour | `RunImmediately = false` |

Both classes derive from `RefreshAheadMemoryCache<TSnapshot>`, which implements `IRefreshAheadMemoryCache<TSnapshot>` and publishes whole snapshots by reference replacement. The sample calls the generated no-argument `AddRefreshAheadMemoryCaches()` method in `Program.cs`.

## Run locally

```sh
dotnet run --project RefreshAhead.MemoryCache.Sample
```

From this folder:

```sh
cd RefreshAhead.MemoryCache.Sample
dotnet run
```

## Run with Docker

```sh
cd samples
docker compose up --build
```

Watch for `PriceCache refreshed` every second. `ReferenceDataCache` is scheduled hourly and does not run immediately in the sample.
