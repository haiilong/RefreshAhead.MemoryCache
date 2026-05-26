namespace RefreshAhead.MemoryCache;

/// <summary>
/// Defines an in-memory cache that owns its refresh-ahead schedule and exposes its current snapshot.
/// </summary>
/// <typeparam name="TSnapshot">The type returned by the cache's primary snapshot getter.</typeparam>
public interface IRefreshAheadMemoryCache<out TSnapshot>
{
    /// <summary>
    /// Gets the refresh schedule for this cache.
    /// </summary>
    RefreshAheadSchedule Schedule { get; }

    /// <summary>
    /// Gets whether the cache should refresh as soon as the host starts.
    /// </summary>
    bool RunImmediately => true;

    /// <summary>
    /// Gets the current in-memory snapshot.
    /// </summary>
    TSnapshot Snapshot { get; }

    /// <summary>
    /// Refreshes the in-memory snapshot.
    /// </summary>
    ValueTask RefreshCacheAsync(CancellationToken cancellationToken);
}
