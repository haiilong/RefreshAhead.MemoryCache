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

    /// <summary>
    /// Adds a health check scoped to the named refresh-ahead memory cache jobs.
    /// </summary>
    /// <param name="builder">The health-checks builder.</param>
    /// <param name="cacheNames">The cache job names to inspect.</param>
    /// <param name="name">The health-check name.</param>
    /// <param name="failureStatus">The status reported for missing, faulted, or overdue cold caches.</param>
    /// <param name="tags">Optional tags used to filter the health check.</param>
    /// <param name="maxAge">Optional maximum age of the last successful refresh before reporting Degraded.</param>
    public static IHealthChecksBuilder AddRefreshAheadMemoryCaches(
        this IHealthChecksBuilder builder,
        IEnumerable<string> cacheNames,
        string name = DefaultName,
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null,
        TimeSpan? maxAge = null)
    {
        ArgumentNullException.ThrowIfNull(cacheNames);
        var names = cacheNames.ToHashSet(StringComparer.Ordinal);
        if (names.Count == 0 || names.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("At least one non-blank cache job name is required.", nameof(cacheNames));
        }

        return AddFiltered(builder, names, maxAge, name, failureStatus, tags);
    }

    /// <summary>
    /// Adds an aggregate health check that reports Degraded when a cache's last successful refresh exceeds
    /// <paramref name="maxAge"/>.
    /// </summary>
    public static IHealthChecksBuilder AddRefreshAheadMemoryCaches(
        this IHealthChecksBuilder builder,
        TimeSpan maxAge,
        string name = DefaultName,
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null) =>
        AddFiltered(builder, null, maxAge, name, failureStatus, tags);

    private static IHealthChecksBuilder AddFiltered(
        IHealthChecksBuilder builder,
        IReadOnlySet<string>? cacheNames,
        TimeSpan? maxAge,
        string name,
        HealthStatus? failureStatus,
        IEnumerable<string>? tags)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (maxAge is not null && maxAge <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAge), maxAge, "Maximum age must be greater than zero.");
        }

        builder.Services.AddRefreshAheadMemoryCacheStatusTracking();
        return builder.Add(new HealthCheckRegistration(
            name,
            serviceProvider => new RefreshAheadMemoryCacheHealthCheck(
                serviceProvider.GetRequiredService<IRefreshAheadMemoryCacheStatusProvider>(),
                serviceProvider.GetRequiredService<TimeProvider>(),
                cacheNames,
                maxAge),
            failureStatus,
            tags));
    }
}
