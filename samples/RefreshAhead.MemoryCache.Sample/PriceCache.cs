using System.Collections.Frozen;

namespace RefreshAhead.MemoryCache.Sample;

public sealed partial class PriceCache(TimeProvider timeProvider, ILogger<PriceCache> logger)
    : RefreshAheadMemoryCache<IReadOnlyDictionary<string, decimal>>(FrozenDictionary<string, decimal>.Empty)
{
    public override RefreshAheadSchedule Schedule => RefreshAheadSchedule.FixedDelay(TimeSpan.FromSeconds(1));

    // WarmUpOnStart is opt-in and defaults to false; most caches should leave it off and rely on a
    // readiness health check instead. This cache turns it on because serving an empty price table would
    // produce wrong answers, not just a cold start, so the host should not accept traffic until the first
    // load succeeds. In production, pair it with RefreshAheadMemoryCacheOptions.WarmUpTimeout.
    public override bool WarmUpOnStart => true;

    protected override async ValueTask<IReadOnlyDictionary<string, decimal>> RefreshSnapshotAsync(
        CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(25), timeProvider, cancellationToken);

        var now = timeProvider.GetUtcNow();
        var next = new Dictionary<string, decimal>
        {
            ["AAPL"] = 180m + now.Second,
            ["MSFT"] = 420m + now.Second,
            ["NVDA"] = 900m + now.Second
        };

        LogPriceCacheJob(logger, now, next.Count);
        return next.ToFrozenDictionary();
    }

    [LoggerMessage(LogLevel.Information, "PriceCache refreshed at {timestamp}. Items: {count}")]
    static partial void LogPriceCacheJob(ILogger<PriceCache> logger, DateTimeOffset timestamp, int count);
}
