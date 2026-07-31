using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace RefreshAhead.MemoryCache;

/// <summary>
/// Thread-safe registry that tracks the refresh history of every cache job and exposes it through
/// <see cref="IRefreshAheadMemoryCacheStatusProvider"/>. One <see cref="StatusEntry"/> is created per job.
/// </summary>
internal sealed class RefreshAheadMemoryCacheStatusRegistry : IRefreshAheadMemoryCacheStatusProvider
{
    private readonly ConcurrentDictionary<string, StatusEntry> _entries = new(StringComparer.Ordinal);

    public StatusEntry Register(string name, bool warmUpOnStart)
    {
        var entry = new StatusEntry(name, warmUpOnStart);
        if (!_entries.TryAdd(name, entry))
        {
            throw new InvalidOperationException(
                $"A refresh-ahead memory cache job named '{name}' is already registered. Job names must be unique.");
        }

        return entry;
    }

    public IReadOnlyList<RefreshAheadMemoryCacheStatus> GetStatuses() =>
        _entries.Values
            .Select(static entry => entry.Snapshot())
            .OrderBy(static status => status.Name, StringComparer.Ordinal)
            .ToArray();

    public bool TryGetStatus(string name, [MaybeNullWhen(false)] out RefreshAheadMemoryCacheStatus status)
    {
        if (_entries.TryGetValue(name, out var entry))
        {
            status = entry.Snapshot();
            return true;
        }

        status = null;
        return false;
    }

    /// <summary>
    /// Mutable per-job counters guarded by a lock. Updates happen once per refresh, never on the read path.
    /// </summary>
    internal sealed class StatusEntry(string name, bool warmUpOnStart)
    {
        private readonly object _gate = new();
        private bool _isRefreshing;
        private bool _isRefreshLoopFaulted;
        private DateTimeOffset? _lastRefreshStartedAt;
        private TimeSpan? _lastRefreshDuration;
        private DateTimeOffset? _nextScheduledRunAt;
        private DateTimeOffset? _lastSuccessAt;
        private DateTimeOffset? _lastFailureAt;
        private string? _lastError;
        private string? _refreshLoopError;
        private long _refreshCount;
        private long _successCount;
        private long _failureCount;
        private long _consecutiveFailures;

        public string Name { get; } = name;

        public void RecordStart(DateTimeOffset startedAt)
        {
            lock (_gate)
            {
                _lastRefreshStartedAt = startedAt;
                _nextScheduledRunAt = null;
                _isRefreshing = true;
                _refreshCount++;
            }
        }

        public void RecordSuccess(DateTimeOffset succeededAt)
        {
            lock (_gate)
            {
                _lastRefreshDuration = GetDuration(succeededAt);
                _lastSuccessAt = succeededAt;
                _isRefreshing = false;
                _successCount++;
                _consecutiveFailures = 0;
                _lastError = null;
            }
        }

        public void RecordFailure(DateTimeOffset failedAt, Exception exception)
        {
            lock (_gate)
            {
                _lastRefreshDuration = GetDuration(failedAt);
                _lastFailureAt = failedAt;
                _isRefreshing = false;
                _failureCount++;
                _consecutiveFailures++;
                _lastError = exception.Message;
            }
        }

        public void RecordNextRun(DateTimeOffset nextRunAt)
        {
            lock (_gate)
            {
                _nextScheduledRunAt = nextRunAt;
            }
        }

        public void RecordStopped()
        {
            lock (_gate)
            {
                _isRefreshing = false;
                _nextScheduledRunAt = null;
            }
        }

        public void RecordLoopFault(Exception exception)
        {
            lock (_gate)
            {
                _isRefreshing = false;
                _nextScheduledRunAt = null;
                _isRefreshLoopFaulted = true;
                _refreshLoopError = exception.Message;
            }
        }

        public RefreshAheadMemoryCacheStatus Snapshot()
        {
            lock (_gate)
            {
                return new RefreshAheadMemoryCacheStatus
                {
                    Name = Name,
                    WarmUpOnStart = warmUpOnStart,
                    IsRefreshing = _isRefreshing,
                    IsRefreshLoopFaulted = _isRefreshLoopFaulted,
                    LastRefreshStartedAt = _lastRefreshStartedAt,
                    LastRefreshDuration = _lastRefreshDuration,
                    NextScheduledRunAt = _nextScheduledRunAt,
                    LastSuccessAt = _lastSuccessAt,
                    LastFailureAt = _lastFailureAt,
                    LastError = _lastError,
                    RefreshLoopError = _refreshLoopError,
                    RefreshCount = _refreshCount,
                    SuccessCount = _successCount,
                    FailureCount = _failureCount,
                    ConsecutiveFailures = _consecutiveFailures,
                };
            }
        }

        private TimeSpan? GetDuration(DateTimeOffset completedAt)
        {
            if (_lastRefreshStartedAt is not { } startedAt)
            {
                return null;
            }

            var duration = completedAt - startedAt;
            return duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        }
    }
}
