using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace RefreshAhead.MemoryCache;

/// <summary>
/// Dependency injection extensions for refresh-ahead memory cache jobs.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the refresh-ahead memory cache hosted service and registers manually configured cache refresh jobs.
    /// </summary>
    public static IServiceCollection AddRefreshAheadMemoryCaches(
        this IServiceCollection services,
        Action<IRefreshAheadMemoryCacheBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, RefreshAheadMemoryCacheService>());

        configure(new RefreshAheadMemoryCacheBuilder(services));
        return services;
    }
}
