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
        services.TryAddSingleton<TCache>();
        services.AddSingleton<IRefreshAheadMemoryCache<TSnapshot>>(serviceProvider => serviceProvider.GetRequiredService<TCache>());
        services.AddSingleton<IRefreshAheadMemoryCacheJob>(
            serviceProvider => RefreshAheadMemoryCacheJob.Create(
                serviceProvider.GetRequiredService<TCache>(),
                name ?? typeof(TCache).Name));

        return this;
    }

    public IRefreshAheadMemoryCacheBuilder Add(
        string name,
        RefreshAheadSchedule schedule,
        Func<IServiceProvider, CancellationToken, ValueTask> refreshAsync,
        bool runImmediately = true,
        bool warmUpOnStart = false)
    {
        services.AddSingleton<IRefreshAheadMemoryCacheJob>(
            new RefreshAheadMemoryCacheJob(name, schedule, refreshAsync, runImmediately, warmUpOnStart));
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
                await using var scope = serviceProvider.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<TService>();
                await refreshAsync(service, cancellationToken);
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
}
