using System.Diagnostics.CodeAnalysis;

namespace RefreshAhead.MemoryCache;

/// <summary>
/// Exposes the refresh history of every registered refresh-ahead memory cache job.
/// Resolve this singleton from dependency injection to observe warm-up state, last refresh times, and failures.
/// </summary>
public interface IRefreshAheadMemoryCacheStatusProvider
{
    /// <summary>
    /// Gets a snapshot of the status of every registered cache job, ordered by name.
    /// </summary>
    IReadOnlyList<RefreshAheadMemoryCacheStatus> GetStatuses();

    /// <summary>
    /// Gets the status of a single cache job by name.
    /// </summary>
    /// <param name="name">The job name used during registration (the concrete cache type name by default).</param>
    /// <param name="status">The current status when the job is found.</param>
    /// <returns><c>true</c> when a job with the given name is registered; otherwise <c>false</c>.</returns>
    bool TryGetStatus(string name, [MaybeNullWhen(false)] out RefreshAheadMemoryCacheStatus status);
}
