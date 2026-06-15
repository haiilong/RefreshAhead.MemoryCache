using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace RefreshAhead.MemoryCache;

/// <summary>
/// Health-check registration extensions for refresh-ahead memory caches.
/// </summary>
public static class RefreshAheadMemoryCacheHealthCheckBuilderExtensions
{
    /// <summary>
    /// The default name used for the refresh-ahead memory cache health check.
    /// </summary>
    public const string DefaultName = "refresh_ahead_memory_cache";

    /// <summary>
    /// Adds a health check that reports the warm-up and refresh state of every registered refresh-ahead memory cache.
    /// </summary>
    /// <param name="builder">The health-checks builder.</param>
    /// <param name="name">The health-check name. Defaults to <see cref="DefaultName"/>.</param>
    /// <param name="failureStatus">
    /// The status reported when a cache has never refreshed successfully. Defaults to
    /// <see cref="HealthStatus.Unhealthy"/> when <c>null</c>.
    /// </param>
    /// <param name="tags">Optional tags used to filter the health check.</param>
    public static IHealthChecksBuilder AddRefreshAheadMemoryCaches(
        this IHealthChecksBuilder builder,
        string name = DefaultName,
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        builder.Services.AddRefreshAheadMemoryCacheStatusTracking();
        builder.Services.TryAddSingleton<RefreshAheadMemoryCacheHealthCheck>();

        return builder.Add(new HealthCheckRegistration(
            name,
            static serviceProvider => serviceProvider.GetRequiredService<RefreshAheadMemoryCacheHealthCheck>(),
            failureStatus,
            tags));
    }
}
