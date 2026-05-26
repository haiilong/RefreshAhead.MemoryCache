using System.Diagnostics.CodeAnalysis;

namespace RefreshAhead.MemoryCache;

/// <summary>
/// Fluent builder for registering refresh-ahead memory cache jobs.
/// </summary>
public interface IRefreshAheadMemoryCacheBuilder
{
    /// <summary>
    /// Registers a singleton refresh-ahead memory cache that declares its own schedule.
    /// </summary>
    IRefreshAheadMemoryCacheBuilder Add<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TCache,
        TSnapshot>(
        string? name = null)
        where TCache : class, IRefreshAheadMemoryCache<TSnapshot>;

    /// <summary>
    /// Registers a refresh job with direct access to the root service provider.
    /// </summary>
    IRefreshAheadMemoryCacheBuilder Add(
        string name,
        RefreshAheadSchedule schedule,
        Func<IServiceProvider, CancellationToken, ValueTask> refreshAsync,
        bool runImmediately = true);

    /// <summary>
    /// Registers a refresh job for a service resolved from dependency injection.
    /// </summary>
    IRefreshAheadMemoryCacheBuilder Add<TService>(
        RefreshAheadSchedule schedule,
        Func<TService, CancellationToken, ValueTask> refreshAsync,
        string? name = null,
        bool runImmediately = true)
        where TService : notnull;

    /// <summary>
    /// Registers a refresh job for a service resolved from dependency injection.
    /// </summary>
    IRefreshAheadMemoryCacheBuilder Add<TService>(
        RefreshAheadSchedule schedule,
        Func<TService, CancellationToken, Task> refreshAsync,
        string? name = null,
        bool runImmediately = true)
        where TService : notnull;

    /// <summary>
    /// Registers a refresh job for a service resolved from dependency injection.
    /// </summary>
    IRefreshAheadMemoryCacheBuilder Add<TService>(
        RefreshAheadSchedule schedule,
        Func<TService, Task> refreshAsync,
        string? name = null,
        bool runImmediately = true)
        where TService : notnull;

    /// <summary>
    /// Registers a run-then-wait refresh job.
    /// </summary>
    IRefreshAheadMemoryCacheBuilder Every<TService>(
        TimeSpan interval,
        Func<TService, CancellationToken, ValueTask> refreshAsync,
        string? name = null,
        bool runImmediately = true)
        where TService : notnull;

    /// <summary>
    /// Registers a run-then-wait refresh job.
    /// </summary>
    IRefreshAheadMemoryCacheBuilder Every<TService>(
        TimeSpan interval,
        Func<TService, CancellationToken, Task> refreshAsync,
        string? name = null,
        bool runImmediately = true)
        where TService : notnull;

    /// <summary>
    /// Registers a run-then-wait refresh job.
    /// </summary>
    IRefreshAheadMemoryCacheBuilder Every<TService>(
        TimeSpan interval,
        Func<TService, Task> refreshAsync,
        string? name = null,
        bool runImmediately = true)
        where TService : notnull;

    /// <summary>
    /// Registers a fixed-rate refresh job that skips missed ticks when refresh takes longer than the interval.
    /// </summary>
    IRefreshAheadMemoryCacheBuilder FixedRate<TService>(
        TimeSpan interval,
        Func<TService, CancellationToken, ValueTask> refreshAsync,
        string? name = null,
        bool runImmediately = true)
        where TService : notnull;

    /// <summary>
    /// Registers a fixed-rate refresh job that skips missed ticks when refresh takes longer than the interval.
    /// </summary>
    IRefreshAheadMemoryCacheBuilder FixedRate<TService>(
        TimeSpan interval,
        Func<TService, CancellationToken, Task> refreshAsync,
        string? name = null,
        bool runImmediately = true)
        where TService : notnull;

    /// <summary>
    /// Registers a fixed-rate refresh job that skips missed ticks when refresh takes longer than the interval.
    /// </summary>
    IRefreshAheadMemoryCacheBuilder FixedRate<TService>(
        TimeSpan interval,
        Func<TService, Task> refreshAsync,
        string? name = null,
        bool runImmediately = true)
        where TService : notnull;

    /// <summary>
    /// Registers a refresh job that runs once per hour at the given offset from the top of the hour.
    /// </summary>
    IRefreshAheadMemoryCacheBuilder HourlyAt<TService>(
        TimeSpan offset,
        Func<TService, CancellationToken, ValueTask> refreshAsync,
        string? name = null,
        bool runImmediately = true,
        TimeZoneInfo? timeZone = null)
        where TService : notnull;

    /// <summary>
    /// Registers a refresh job that runs once per hour at the given offset from the top of the hour.
    /// </summary>
    IRefreshAheadMemoryCacheBuilder HourlyAt<TService>(
        TimeSpan offset,
        Func<TService, CancellationToken, Task> refreshAsync,
        string? name = null,
        bool runImmediately = true,
        TimeZoneInfo? timeZone = null)
        where TService : notnull;

    /// <summary>
    /// Registers a refresh job that runs once per hour at the given offset from the top of the hour.
    /// </summary>
    IRefreshAheadMemoryCacheBuilder HourlyAt<TService>(
        TimeSpan offset,
        Func<TService, Task> refreshAsync,
        string? name = null,
        bool runImmediately = true,
        TimeZoneInfo? timeZone = null)
        where TService : notnull;

    /// <summary>
    /// Registers a refresh job that runs once per day at the given local time.
    /// </summary>
    IRefreshAheadMemoryCacheBuilder DailyAt<TService>(
        TimeOnly timeOfDay,
        Func<TService, CancellationToken, ValueTask> refreshAsync,
        string? name = null,
        bool runImmediately = true,
        TimeZoneInfo? timeZone = null)
        where TService : notnull;

    /// <summary>
    /// Registers a refresh job that runs once per day at the given local time.
    /// </summary>
    IRefreshAheadMemoryCacheBuilder DailyAt<TService>(
        TimeOnly timeOfDay,
        Func<TService, CancellationToken, Task> refreshAsync,
        string? name = null,
        bool runImmediately = true,
        TimeZoneInfo? timeZone = null)
        where TService : notnull;

    /// <summary>
    /// Registers a refresh job that runs once per day at the given local time.
    /// </summary>
    IRefreshAheadMemoryCacheBuilder DailyAt<TService>(
        TimeOnly timeOfDay,
        Func<TService, Task> refreshAsync,
        string? name = null,
        bool runImmediately = true,
        TimeZoneInfo? timeZone = null)
        where TService : notnull;
}
