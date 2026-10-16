using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace RefreshAhead.MemoryCache;

/// <summary>
/// A health check that reports the warm-up and refresh state of registered refresh-ahead memory caches.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>The registration's failure status when a refresh loop has faulted, or a cache is not warm after it was due to run.</description></item>
/// <item><description>Degraded when a warm cache's latest attempt failed or its last success exceeds the configured maximum age.</description></item>
/// <item><description>Healthy when caches are operating normally, including delayed-first-run caches that are not due yet.</description></item>
/// </list>
/// Register it with <see cref="RefreshAheadMemoryCacheHealthCheckBuilderExtensions.AddRefreshAheadMemoryCaches(Microsoft.Extensions.DependencyInjection.IHealthChecksBuilder,string,HealthStatus?,IEnumerable{string}?)"/>.
/// </remarks>
public sealed class RefreshAheadMemoryCacheHealthCheck : IHealthCheck
{
    private readonly IRefreshAheadMemoryCacheStatusProvider _statusProvider;
    private readonly TimeProvider _timeProvider;
    private readonly IReadOnlySet<string>? _cacheNames;
    private readonly TimeSpan? _maxAge;

    /// <summary>
    /// Creates an aggregate health check using all registered caches and no maximum-age threshold.
    /// </summary>
    public RefreshAheadMemoryCacheHealthCheck(IRefreshAheadMemoryCacheStatusProvider statusProvider)
        : this(statusProvider, TimeProvider.System, null, null)
    {
    }

    /// <summary>
    /// Creates a health check with optional cache-name filtering and a maximum age for the last successful refresh.
    /// </summary>
    public RefreshAheadMemoryCacheHealthCheck(
        IRefreshAheadMemoryCacheStatusProvider statusProvider,
        TimeProvider timeProvider,
        IReadOnlySet<string>? cacheNames,
        TimeSpan? maxAge)
    {
        ArgumentNullException.ThrowIfNull(statusProvider);
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (maxAge is not null && maxAge <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAge), maxAge, "Maximum age must be greater than zero.");
        }

        _statusProvider = statusProvider;
        _timeProvider = timeProvider;
        _cacheNames = cacheNames;
        _maxAge = maxAge;
    }

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var allStatuses = _statusProvider.GetStatuses();
        var statuses = _cacheNames is null
            ? allStatuses
            : allStatuses.Where(status => _cacheNames.Contains(status.Name)).ToArray();

        if (_cacheNames is not null)
        {
            var registeredNames = statuses.Select(static status => status.Name).ToHashSet(StringComparer.Ordinal);
            var missing = _cacheNames.Where(name => !registeredNames.Contains(name)).ToArray();
            if (missing.Length > 0)
            {
                return Task.FromResult(new HealthCheckResult(
                    context.Registration.FailureStatus,
                    $"Refresh-ahead memory cache job(s) not registered: {string.Join(", ", missing)}."));
            }
        }

        if (statuses.Count == 0)
        {
            return Task.FromResult(HealthCheckResult.Healthy("No refresh-ahead memory caches are registered."));
        }

        var data = statuses.ToDictionary(
            static status => status.Name,
            static status => (object)new Dictionary<string, object?>
            {
                ["runImmediately"] = status.RunImmediately,
                ["isWarm"] = status.IsWarm,
                ["isRefreshing"] = status.IsRefreshing,
                ["isRefreshLoopFaulted"] = status.IsRefreshLoopFaulted,
                ["lastRefreshDuration"] = status.LastRefreshDuration,
                ["nextScheduledRunAt"] = status.NextScheduledRunAt,
                ["lastSuccessAt"] = status.LastSuccessAt,
                ["lastFailureAt"] = status.LastFailureAt,
                ["consecutiveFailures"] = status.ConsecutiveFailures,
                ["lastError"] = status.LastError,
                ["refreshLoopError"] = status.RefreshLoopError,
            },
            StringComparer.Ordinal);

        var faulted = statuses
            .Where(static status => status.IsRefreshLoopFaulted)
            .Select(static status => status.Name)
            .ToArray();
        if (faulted.Length > 0)
        {
            return Task.FromResult(new HealthCheckResult(
                context.Registration.FailureStatus,
                $"Refresh-ahead memory cache refresh loop(s) faulted: {string.Join(", ", faulted)}.",
                data: data));
        }

        var now = _timeProvider.GetUtcNow();
        var notWarm = statuses
            .Where(status =>
                !status.IsWarm &&
                (status.WarmUpOnStart ||
                 (status.RunImmediately && status.ConsecutiveFailures > 0) ||
                 status.NextScheduledRunAt is null ||
                 status.NextScheduledRunAt <= now))
            .Select(static status => status.Name)
            .ToArray();
        if (notWarm.Length > 0)
        {
            return Task.FromResult(new HealthCheckResult(
                context.Registration.FailureStatus,
                $"Refresh-ahead memory cache(s) not yet warm: {string.Join(", ", notWarm)}.",
                data: data));
        }

        var stale = statuses
            .Where(status =>
                status.ConsecutiveFailures > 0 ||
                (_maxAge is { } maxAge && status.LastSuccessAt is { } lastSuccessAt && now - lastSuccessAt > maxAge))
            .Select(static status => status.Name)
            .ToArray();
        if (stale.Length > 0)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                $"Refresh-ahead memory cache(s) serving stale data: {string.Join(", ", stale)}.",
                data: data));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            "All refresh-ahead memory caches are healthy or awaiting their first scheduled run.",
            data));
    }
}
