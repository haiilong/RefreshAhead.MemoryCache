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

        AddCore(services);
        configure(new RefreshAheadMemoryCacheBuilder(services));
        return services;
    }

    /// <summary>
    /// Registers the refresh-ahead memory cache status provider (<see cref="IRefreshAheadMemoryCacheStatusProvider"/>)
    /// without adding any cache jobs. Called automatically by <see cref="AddRefreshAheadMemoryCaches"/>; companion
    /// packages (such as the health check) call it to guarantee the provider is available.
    /// </summary>
    public static IServiceCollection AddRefreshAheadMemoryCacheStatusTracking(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<RefreshAheadMemoryCacheStatusRegistry>();
        services.TryAddSingleton<IRefreshAheadMemoryCacheStatusProvider>(
            static serviceProvider => serviceProvider.GetRequiredService<RefreshAheadMemoryCacheStatusRegistry>());
        return services;
    }

    private static void AddCore(IServiceCollection services)
    {
        services.AddOptions<RefreshAheadMemoryCacheOptions>()
            .Validate(
                static options => options.WarmUpTimeout is null || options.WarmUpTimeout > TimeSpan.Zero,
                $"{nameof(RefreshAheadMemoryCacheOptions.WarmUpTimeout)} must be greater than zero when set.");
        services.AddRefreshAheadMemoryCacheStatusTracking();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, RefreshAheadMemoryCacheService>());
    }
}
