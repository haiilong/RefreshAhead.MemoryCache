using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace RefreshAhead.MemoryCache;

/// <summary>
/// A health check that reports the warm-up and refresh state of every registered refresh-ahead memory cache.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>Unhealthy (the registration's failure status) when any cache has never refreshed
/// successfully and is still serving its initial snapshot.</description></item>
/// <item><description>Degraded when every cache is warm but at least one is currently serving stale data after
/// a failed refresh.</description></item>
/// <item><description>Healthy when every cache is warm and its most recent refresh succeeded.</description></item>
/// </list>
/// Register it with <see cref="RefreshAheadMemoryCacheHealthCheckBuilderExtensions.AddRefreshAheadMemoryCaches"/>.
/// </remarks>
public sealed class RefreshAheadMemoryCacheHealthCheck(IRefreshAheadMemoryCacheStatusProvider statusProvider) : IHealthCheck
{
    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var statuses = statusProvider.GetStatuses();
        if (statuses.Count == 0)
        {
            return Task.FromResult(HealthCheckResult.Healthy("No refresh-ahead memory caches are registered."));
        }

        var data = statuses.ToDictionary(
            static status => status.Name,
            static status => (object)new Dictionary<string, object?>
            {
                ["isWarm"] = status.IsWarm,
                ["lastSuccessAt"] = status.LastSuccessAt,
                ["lastFailureAt"] = status.LastFailureAt,
                ["consecutiveFailures"] = status.ConsecutiveFailures,
                ["lastError"] = status.LastError,
            },
            StringComparer.Ordinal);

        var notWarm = statuses.Where(static status => !status.IsWarm).Select(static status => status.Name).ToArray();
        if (notWarm.Length > 0)
        {
            return Task.FromResult(new HealthCheckResult(
                context.Registration.FailureStatus,
                $"Refresh-ahead memory cache(s) not yet warm: {string.Join(", ", notWarm)}.",
                data: data));
        }

        var stale = statuses.Where(static status => status.ConsecutiveFailures > 0).Select(static status => status.Name).ToArray();
        if (stale.Length > 0)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                $"Refresh-ahead memory cache(s) serving stale data after a failed refresh: {string.Join(", ", stale)}.",
                data: data));
        }

        return Task.FromResult(HealthCheckResult.Healthy("All refresh-ahead memory caches are warm.", data));
    }
}
