using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace RefreshAhead.MemoryCache;

internal sealed class RefreshAheadMemoryCacheBuilder(IServiceCollection services) : IRefreshAheadMemoryCacheBuilder
{
    public IRefreshAheadMemoryCacheBuilder Add<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TCache,
        TSnapshot>(
        string? name = null)
        where TCache : class, IRefreshAheadMemoryCache<TSnapshot>
    {
        var jobName = name ?? typeof(TCache).Name;
        var existingCacheRegistration = GetRegistrations()
            .FirstOrDefault(registration => registration.CacheType == typeof(TCache));
        if (existingCacheRegistration is not null)
        {
            if (existingCacheRegistration.SnapshotType != typeof(TSnapshot))
            {
                throw new InvalidOperationException(
                    $"Cache type '{typeof(TCache)}' is already registered with snapshot type '{existingCacheRegistration.SnapshotType}' and cannot also be registered with '{typeof(TSnapshot)}'.");
            }

            if (StringComparer.Ordinal.Equals(existingCacheRegistration.Name, jobName))
            {
                return this;
            }

            throw new InvalidOperationException(
                $"Cache type '{typeof(TCache)}' is already registered as job '{existingCacheRegistration.Name}' and cannot also be registered as '{jobName}'.");
        }

        EnsureUniqueJobName(jobName);
        services.TryAddSingleton<TCache>();
        services.AddKeyedSingleton<IRefreshAheadMemoryCache<TSnapshot>>(
            jobName,
            static (serviceProvider, _) => serviceProvider.GetRequiredService<TCache>());

        ServiceDescriptor? aliasDescriptor = null;
        var snapshotRegistrations = GetRegistrations()
            .Where(registration => registration.SnapshotType == typeof(TSnapshot))
            .ToArray();
        if (snapshotRegistrations.Length == 0)
        {
            aliasDescriptor = ServiceDescriptor.Singleton(
                typeof(IRefreshAheadMemoryCache<TSnapshot>),
                static serviceProvider => serviceProvider.GetRequiredService<TCache>());
            services.Add(aliasDescriptor);
        }
        else
        {
            foreach (var registration in snapshotRegistrations)
            {
                if (registration.AliasDescriptor is not null)
                {
                    services.Remove(registration.AliasDescriptor);
                }
            }

            if (!GetSnapshotCollisions().Any(collision => collision.SnapshotType == typeof(TSnapshot)))
            {
                var collisionDescriptor = ServiceDescriptor.Singleton(
                    typeof(IRefreshAheadMemoryCache<TSnapshot>),
                    _ => throw new InvalidOperationException(
                        $"Multiple refresh-ahead memory caches use snapshot type '{typeof(TSnapshot)}'. Resolve the concrete cache type or use GetRequiredKeyedService with the cache job name."));
                services.Add(collisionDescriptor);
                services.AddSingleton(new SnapshotAliasCollision(typeof(TSnapshot)));
            }
        }

        services.AddSingleton<IRefreshAheadMemoryCacheJob>(
            serviceProvider => RefreshAheadMemoryCacheJob.Create(
                serviceProvider.GetRequiredService<TCache>(),
                jobName));
        services.AddSingleton(new CacheRegistration(typeof(TCache), typeof(TSnapshot), jobName, aliasDescriptor));

        return this;
    }

    public IRefreshAheadMemoryCacheBuilder Add(
        string name,
        RefreshAheadSchedule schedule,
        Func<IServiceProvider, CancellationToken, ValueTask> refreshAsync,
        bool runImmediately = true,
        bool warmUpOnStart = false)
    {
        EnsureUniqueJobName(name);
        services.AddSingleton<IRefreshAheadMemoryCacheJob>(
            new RefreshAheadMemoryCacheJob(name, schedule, refreshAsync, runImmediately, warmUpOnStart));
        services.AddSingleton(new CacheRegistration(null, null, name, null));
        return this;
    }

    public IRefreshAheadMemoryCacheBuilder Add<TService>(
        RefreshAheadSchedule schedule,
        Func<TService, CancellationToken, ValueTask> refreshAsync,
        string? name = null,
        bool runImmediately = true,
        bool warmUpOnStart = false)
        where TService : notnull
    {
        ArgumentNullException.ThrowIfNull(refreshAsync);

        return Add(
            name ?? typeof(TService).Name,
            schedule,
            async (serviceProvider, cancellationToken) =>
            {
                var scope = serviceProvider.CreateAsyncScope();
                await using (scope.ConfigureAwait(false))
                {
                    var service = scope.ServiceProvider.GetRequiredService<TService>();
                    await refreshAsync(service, cancellationToken).ConfigureAwait(false);
                }
            },
            runImmediately,
            warmUpOnStart);
    }

    public IRefreshAheadMemoryCacheBuilder Add<TService>(
        RefreshAheadSchedule schedule,
        Func<TService, CancellationToken, Task> refreshAsync,
        string? name = null,
        bool runImmediately = true,
        bool warmUpOnStart = false)
        where TService : notnull
    {
        ArgumentNullException.ThrowIfNull(refreshAsync);

        return Add<TService>(
            schedule,
            (service, cancellationToken) => new ValueTask(refreshAsync(service, cancellationToken)),
            name,
            runImmediately,
            warmUpOnStart);
    }

    public IRefreshAheadMemoryCacheBuilder Add<TService>(
        RefreshAheadSchedule schedule,
        Func<TService, Task> refreshAsync,
        string? name = null,
        bool runImmediately = true,
        bool warmUpOnStart = false)
        where TService : notnull
    {
        ArgumentNullException.ThrowIfNull(refreshAsync);

        return Add<TService>(
            schedule,
            (service, _) => refreshAsync(service),
            name,
            runImmediately,
            warmUpOnStart);
    }

    public IRefreshAheadMemoryCacheBuilder Every<TService>(
        TimeSpan interval,
        Func<TService, CancellationToken, ValueTask> refreshAsync,
        string? name = null,
        bool runImmediately = true,
        bool warmUpOnStart = false)
        where TService : notnull =>
        Add(RefreshAheadSchedule.FixedDelay(interval), refreshAsync, name, runImmediately, warmUpOnStart);

    public IRefreshAheadMemoryCacheBuilder Every<TService>(
        TimeSpan interval,
        Func<TService, CancellationToken, Task> refreshAsync,
        string? name = null,
        bool runImmediately = true,
        bool warmUpOnStart = false)
        where TService : notnull =>
        Add(RefreshAheadSchedule.FixedDelay(interval), refreshAsync, name, runImmediately, warmUpOnStart);

    public IRefreshAheadMemoryCacheBuilder Every<TService>(
        TimeSpan interval,
        Func<TService, Task> refreshAsync,
        string? name = null,
        bool runImmediately = true,
        bool warmUpOnStart = false)
        where TService : notnull =>
        Add(RefreshAheadSchedule.FixedDelay(interval), refreshAsync, name, runImmediately, warmUpOnStart);

    public IRefreshAheadMemoryCacheBuilder FixedRate<TService>(
        TimeSpan interval,
        Func<TService, CancellationToken, ValueTask> refreshAsync,
        string? name = null,
        bool runImmediately = true,
        bool warmUpOnStart = false)
        where TService : notnull =>
        Add(RefreshAheadSchedule.FixedRate(interval), refreshAsync, name, runImmediately, warmUpOnStart);

    public IRefreshAheadMemoryCacheBuilder FixedRate<TService>(
        TimeSpan interval,
        Func<TService, CancellationToken, Task> refreshAsync,
        string? name = null,
        bool runImmediately = true,
        bool warmUpOnStart = false)
        where TService : notnull =>
        Add(RefreshAheadSchedule.FixedRate(interval), refreshAsync, name, runImmediately, warmUpOnStart);

    public IRefreshAheadMemoryCacheBuilder FixedRate<TService>(
        TimeSpan interval,
        Func<TService, Task> refreshAsync,
        string? name = null,
        bool runImmediately = true,
        bool warmUpOnStart = false)
        where TService : notnull =>
        Add(RefreshAheadSchedule.FixedRate(interval), refreshAsync, name, runImmediately, warmUpOnStart);

    public IRefreshAheadMemoryCacheBuilder HourlyAt<TService>(
        TimeSpan offset,
        Func<TService, CancellationToken, ValueTask> refreshAsync,
        string? name = null,
        bool runImmediately = true,
        TimeZoneInfo? timeZone = null,
        bool warmUpOnStart = false)
        where TService : notnull =>
        Add(RefreshAheadSchedule.HourlyAt(offset, timeZone), refreshAsync, name, runImmediately, warmUpOnStart);

    public IRefreshAheadMemoryCacheBuilder HourlyAt<TService>(
        TimeSpan offset,
        Func<TService, CancellationToken, Task> refreshAsync,
        string? name = null,
        bool runImmediately = true,
        TimeZoneInfo? timeZone = null,
        bool warmUpOnStart = false)
        where TService : notnull =>
        Add(RefreshAheadSchedule.HourlyAt(offset, timeZone), refreshAsync, name, runImmediately, warmUpOnStart);

    public IRefreshAheadMemoryCacheBuilder HourlyAt<TService>(
        TimeSpan offset,
        Func<TService, Task> refreshAsync,
        string? name = null,
        bool runImmediately = true,
        TimeZoneInfo? timeZone = null,
        bool warmUpOnStart = false)
        where TService : notnull =>
        Add(RefreshAheadSchedule.HourlyAt(offset, timeZone), refreshAsync, name, runImmediately, warmUpOnStart);

    public IRefreshAheadMemoryCacheBuilder DailyAt<TService>(
        TimeOnly timeOfDay,
        Func<TService, CancellationToken, ValueTask> refreshAsync,
        string? name = null,
        bool runImmediately = true,
        TimeZoneInfo? timeZone = null,
        bool warmUpOnStart = false)
        where TService : notnull =>
        Add(RefreshAheadSchedule.DailyAt(timeOfDay, timeZone), refreshAsync, name, runImmediately, warmUpOnStart);

    public IRefreshAheadMemoryCacheBuilder DailyAt<TService>(
        TimeOnly timeOfDay,
        Func<TService, CancellationToken, Task> refreshAsync,
        string? name = null,
        bool runImmediately = true,
        TimeZoneInfo? timeZone = null,
        bool warmUpOnStart = false)
        where TService : notnull =>
        Add(RefreshAheadSchedule.DailyAt(timeOfDay, timeZone), refreshAsync, name, runImmediately, warmUpOnStart);

    public IRefreshAheadMemoryCacheBuilder DailyAt<TService>(
        TimeOnly timeOfDay,
        Func<TService, Task> refreshAsync,
        string? name = null,
        bool runImmediately = true,
        TimeZoneInfo? timeZone = null,
        bool warmUpOnStart = false)
        where TService : notnull =>
        Add(RefreshAheadSchedule.DailyAt(timeOfDay, timeZone), refreshAsync, name, runImmediately, warmUpOnStart);

    private IEnumerable<CacheRegistration> GetRegistrations() =>
        services
            .Where(static descriptor => descriptor.ServiceType == typeof(CacheRegistration))
            .Select(static descriptor => (CacheRegistration)descriptor.ImplementationInstance!);

    private IEnumerable<SnapshotAliasCollision> GetSnapshotCollisions() =>
        services
            .Where(static descriptor => descriptor.ServiceType == typeof(SnapshotAliasCollision))
            .Select(static descriptor => (SnapshotAliasCollision)descriptor.ImplementationInstance!);

    private void EnsureUniqueJobName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (GetRegistrations().Any(registration => StringComparer.Ordinal.Equals(registration.Name, name)))
        {
            throw new InvalidOperationException(
                $"A refresh-ahead memory cache job named '{name}' is already registered. Job names must be unique.");
        }
    }

    private sealed record CacheRegistration(
        Type? CacheType,
        Type? SnapshotType,
        string Name,
        ServiceDescriptor? AliasDescriptor);

    private sealed record SnapshotAliasCollision(Type SnapshotType);
}
