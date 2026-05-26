namespace RefreshAhead.MemoryCache;

/// <summary>
/// Base class for refresh-ahead memory caches that publish a whole immutable snapshot per refresh.
/// </summary>
/// <typeparam name="TSnapshot">The reference-type snapshot exposed by the cache.</typeparam>
public abstract class RefreshAheadMemoryCache<TSnapshot>(TSnapshot initialSnapshot) : IRefreshAheadMemoryCache<TSnapshot>
    where TSnapshot : class
{
    private TSnapshot _snapshot = initialSnapshot ?? throw new ArgumentNullException(nameof(initialSnapshot));

    /// <inheritdoc />
    public abstract RefreshAheadSchedule Schedule { get; }

    /// <inheritdoc />
    public virtual bool RunImmediately => true;

    /// <inheritdoc />
    public TSnapshot Snapshot => Volatile.Read(ref _snapshot);

    /// <inheritdoc />
    public virtual async ValueTask RefreshCacheAsync(CancellationToken cancellationToken)
    {
        var next = await RefreshSnapshotAsync(cancellationToken);
        PublishSnapshot(next);
    }

    /// <summary>
    /// Creates the next complete snapshot. The base class publishes it atomically after this method succeeds.
    /// </summary>
    protected abstract ValueTask<TSnapshot> RefreshSnapshotAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Publishes a new complete snapshot.
    /// </summary>
    protected virtual void PublishSnapshot(TSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Volatile.Write(ref _snapshot, snapshot);
    }
}
