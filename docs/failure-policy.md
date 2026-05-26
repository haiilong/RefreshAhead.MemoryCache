# Failure Policy

`RefreshAhead.MemoryCache` does not decide what happens to your snapshot when a refresh fails.

The scheduler knows that `RefreshCacheAsync` threw. It can log the failure and schedule the next run. It cannot know whether your callers are safer with stale data, empty data, fallback data, or a partially rebuilt snapshot. That decision belongs to the cache.

## Default Recommendation: Keep Last Good Snapshot

For most refresh-ahead caches, the safest default is to build the next snapshot locally and only swap after the load succeeds.

```csharp
protected override async ValueTask<IReadOnlyList<Country>> RefreshSnapshotAsync(
    CancellationToken cancellationToken)
{
    var next = await LoadSnapshotAsync(cancellationToken);
    return Array.AsReadOnly(next.ToArray());
}
```

If `LoadSnapshotAsync` throws, the base class never publishes a new snapshot and callers keep reading the last good value. The scheduler logs the exception and tries again on the next scheduled run.

This is usually right for:

- reference data;
- prices or quotes where stale is better than empty;
- permission snapshots where dropping everything would break healthy requests;
- feature/config snapshots where the previous value is known-good.

## Reset Immediately on Failure

Sometimes empty is safer than stale. In that case, reset the snapshot inside the cache and rethrow.

```csharp
public async ValueTask RefreshCacheAsync(CancellationToken cancellationToken)
{
    try
    {
        await base.RefreshCacheAsync(cancellationToken);
    }
    catch
    {
        PublishSnapshot(Array.Empty<Country>());

        throw;
    }
}
```

Rethrowing matters. It lets the scheduler log the failed refresh and keeps operational signals honest.

## Reset After N Consecutive Failures

This is a useful middle ground: keep the last good snapshot for short transient failures, then clear it after repeated failures.

```csharp
private int _consecutiveFailures;

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
            PublishSnapshot(Array.Empty<Country>());
        }

        throw;
    }
}
```

Use this when stale data is acceptable briefly but dangerous after a longer outage.

## Use a Fallback Snapshot

Some caches have a safe fallback value that is better than both stale and empty.

```csharp
catch
{
    PublishSnapshot(CreateFallbackSnapshot());

    throw;
}
```

This is domain-specific. Keep the fallback obvious and test it.

## Do Not Mutate the Live Snapshot While Loading

Avoid clearing or partially mutating the live snapshot before the new data is ready.

```csharp
// Avoid this shape if you implement the interface directly and expose a mutable dictionary.
Snapshot.Clear();

foreach (var item in await LoadItemsAsync(cancellationToken))
{
    Snapshot[item.Id] = item;
}
```

If the load fails halfway through, callers observe a broken intermediate state. Prefer building a separate `next` value and swapping once.

## Swallowing Exceptions

You can catch and swallow exceptions inside `RefreshCacheAsync`, but then the scheduler sees the refresh as successful. That may be useful in rare cases, but usually it hides operational failures.

Prefer:

1. Apply your snapshot policy.
2. Rethrow.
3. Let the scheduler log the failure and continue.

## Where This Boundary Sits

The library owns:

- scheduling;
- non-overlap;
- DI registration;
- cancellation;
- refresh failure logging.

The cache owns:

- snapshot type;
- snapshot concurrency;
- stale-vs-empty-vs-fallback policy;
- partial refresh behavior;
- domain-specific recovery.
