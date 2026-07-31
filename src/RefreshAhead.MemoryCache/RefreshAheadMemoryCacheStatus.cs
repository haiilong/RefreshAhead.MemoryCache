namespace RefreshAhead.MemoryCache;

/// <summary>
/// An immutable point-in-time view of a single refresh-ahead memory cache job's refresh history.
/// </summary>
public sealed record RefreshAheadMemoryCacheStatus
{
    /// <summary>
    /// Gets the job name, inferred from the concrete cache type name or supplied explicitly during registration.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets whether the cache has completed at least one successful refresh. While this is <c>false</c>,
    /// callers are still reading the initial snapshot supplied at construction.
    /// </summary>
    public bool IsWarm => SuccessCount > 0;

    /// <summary>
    /// Gets whether this cache must complete its first refresh before host startup completes.
    /// </summary>
    public bool WarmUpOnStart { get; init; }

    /// <summary>
    /// Gets whether a refresh attempt is currently running.
    /// </summary>
    public bool IsRefreshing { get; init; }

    /// <summary>
    /// Gets whether the refresh loop terminated unexpectedly.
    /// </summary>
    public bool IsRefreshLoopFaulted { get; init; }

    /// <summary>
    /// Gets the UTC time the most recent refresh attempt started, or <c>null</c> if none has started.
    /// </summary>
    public DateTimeOffset? LastRefreshStartedAt { get; init; }

    /// <summary>
    /// Gets the duration of the most recently completed refresh attempt, or <c>null</c> if none has completed.
    /// </summary>
    public TimeSpan? LastRefreshDuration { get; init; }

    /// <summary>
    /// Gets the UTC time at which the next refresh is scheduled, or <c>null</c> when refreshing or stopped.
    /// </summary>
    public DateTimeOffset? NextScheduledRunAt { get; init; }

    /// <summary>
    /// Gets the UTC time of the most recent successful refresh, or <c>null</c> if none has succeeded.
    /// </summary>
    public DateTimeOffset? LastSuccessAt { get; init; }

    /// <summary>
    /// Gets the UTC time of the most recent failed refresh, or <c>null</c> if none has failed.
    /// </summary>
    public DateTimeOffset? LastFailureAt { get; init; }

    /// <summary>
    /// Gets the message of the most recent refresh failure, or <c>null</c> if the most recent refresh succeeded
    /// or none has run. The full exception is written to the logs at <c>Error</c> level.
    /// </summary>
    public string? LastError { get; init; }

    /// <summary>
    /// Gets the unexpected refresh-loop termination message, or <c>null</c> while the loop is operating normally.
    /// </summary>
    public string? RefreshLoopError { get; init; }

    /// <summary>
    /// Gets the total number of refresh attempts that have started.
    /// </summary>
    public long RefreshCount { get; init; }

    /// <summary>
    /// Gets the total number of refreshes that have completed successfully.
    /// </summary>
    public long SuccessCount { get; init; }

    /// <summary>
    /// Gets the total number of refreshes that have failed.
    /// </summary>
    public long FailureCount { get; init; }

    /// <summary>
    /// Gets the number of consecutive failures since the last success. Reset to zero on every successful refresh.
    /// </summary>
    public long ConsecutiveFailures { get; init; }
}
