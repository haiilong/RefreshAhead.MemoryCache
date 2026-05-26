namespace RefreshAhead.MemoryCache.Sample;

public sealed partial class ReferenceDataCache(TimeProvider timeProvider, ILogger<ReferenceDataCache> logger)
    : RefreshAheadMemoryCache<IReadOnlyList<string>>([])
{
    public override RefreshAheadSchedule Schedule => RefreshAheadSchedule.HourlyAt(TimeSpan.FromSeconds(10), TimeZoneInfo.Local);

    public override bool RunImmediately => false;

    protected override async ValueTask<IReadOnlyList<string>> RefreshSnapshotAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(100), timeProvider, cancellationToken);

        string[] next = ["equities", "futures", "options"];

        LogReferenceDataCache(logger, timeProvider.GetUtcNow(), next.Length);

        return Array.AsReadOnly(next);
    }

    [LoggerMessage(LogLevel.Information, "ReferenceDataCache refreshed at {timestamp}. Items: {count}")]
    static partial void LogReferenceDataCache(ILogger<ReferenceDataCache> logger, DateTimeOffset timestamp, int count);
}
