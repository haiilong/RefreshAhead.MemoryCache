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

    public StatusEntry GetOrAdd(string name) => _entries.GetOrAdd(name, static n => new StatusEntry(n));

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
    internal sealed class StatusEntry(string name)
    {
        private readonly object _gate = new();
        private DateTimeOffset? _lastRefreshStartedAt;
        private DateTimeOffset? _lastSuccessAt;
        private DateTimeOffset? _lastFailureAt;
        private string? _lastError;
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
                _refreshCount++;
            }
        }

        public void RecordSuccess(DateTimeOffset succeededAt)
        {
            lock (_gate)
            {
                _lastSuccessAt = succeededAt;
                _successCount++;
                _consecutiveFailures = 0;
                _lastError = null;
            }
        }

        public void RecordFailure(DateTimeOffset failedAt, Exception exception)
        {
            lock (_gate)
            {
                _lastFailureAt = failedAt;
                _failureCount++;
                _consecutiveFailures++;
                _lastError = exception.Message;
            }
        }

        public RefreshAheadMemoryCacheStatus Snapshot()
        {
            lock (_gate)
            {
                return new RefreshAheadMemoryCacheStatus
                {
                    Name = Name,
                    LastRefreshStartedAt = _lastRefreshStartedAt,
                    LastSuccessAt = _lastSuccessAt,
                    LastFailureAt = _lastFailureAt,
                    LastError = _lastError,
                    RefreshCount = _refreshCount,
                    SuccessCount = _successCount,
                    FailureCount = _failureCount,
                    ConsecutiveFailures = _consecutiveFailures,
                };
            }
        }
    }
}
