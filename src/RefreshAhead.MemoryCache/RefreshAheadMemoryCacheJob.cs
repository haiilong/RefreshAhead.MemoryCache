namespace RefreshAhead.MemoryCache;

internal interface IRefreshAheadMemoryCacheJob
{
    string Name { get; }

    RefreshAheadSchedule Schedule { get; }

    bool RunImmediately { get; }

    bool WarmUpOnStart { get; }

    ValueTask RefreshAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken);
}

internal sealed class RefreshAheadMemoryCacheJob : IRefreshAheadMemoryCacheJob
{
    private readonly Func<IServiceProvider, CancellationToken, ValueTask> _refreshAsync;

    public static RefreshAheadMemoryCacheJob Create<TSnapshot>(IRefreshAheadMemoryCache<TSnapshot> cache, string name)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new RefreshAheadMemoryCacheJob(
            name,
            cache.Schedule,
            (_, cancellationToken) => cache.RefreshCacheAsync(cancellationToken),
            cache.RunImmediately,
            cache.WarmUpOnStart);
    }

    public RefreshAheadMemoryCacheJob(
        string name,
        RefreshAheadSchedule schedule,
        Func<IServiceProvider, CancellationToken, ValueTask> refreshAsync,
        bool runImmediately,
        bool warmUpOnStart = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(refreshAsync);

        Name = name;
        Schedule = schedule;
        _refreshAsync = refreshAsync;
        RunImmediately = runImmediately;
        WarmUpOnStart = warmUpOnStart;
    }

    public string Name { get; }

    public RefreshAheadSchedule Schedule { get; }

    public bool RunImmediately { get; }

    public bool WarmUpOnStart { get; }

    public ValueTask RefreshAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken) =>
        _refreshAsync(serviceProvider, cancellationToken);
}
