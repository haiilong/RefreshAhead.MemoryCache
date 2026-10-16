using System.Diagnostics.CodeAnalysis;
using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Time.Testing;

namespace RefreshAhead.MemoryCache.Tests;

public sealed class RefreshAheadMemoryCacheHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_WhenNoCachesRegistered_ReturnsHealthy()
    {
        var result = await CheckAsync();

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenCacheNotWarm_ReturnsFailureStatus()
    {
        var result = await CheckAsync(
            Status("not-warm", successCount: 0, consecutiveFailures: 0));

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("not-warm");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenDelayedFirstRunIsNotDue_ReturnsHealthy()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 5, 26, 10, 0, 0, TimeSpan.Zero));
        var result = await CheckAsync(
            timeProvider,
            cacheNames: null,
            maxAge: null,
            Status(
                "not-due",
                successCount: 0,
                consecutiveFailures: 0,
                nextScheduledRunAt: timeProvider.GetUtcNow().AddHours(1)));

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenRefreshLoopFaulted_ReturnsFailureStatus()
    {
        var result = await CheckAsync(
            Status("faulted", successCount: 1, consecutiveFailures: 0, isRefreshLoopFaulted: true));

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("faulted");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenRunImmediatelyFailsBeforeFirstSuccess_ReturnsFailureStatus()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 5, 26, 10, 0, 0, TimeSpan.Zero));
        var result = await CheckAsync(
            timeProvider,
            cacheNames: null,
            maxAge: null,
            Status(
                "hourly",
                successCount: 0,
                consecutiveFailures: 1,
                nextScheduledRunAt: timeProvider.GetUtcNow().AddHours(1),
                runImmediately: true));

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("hourly");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenDelayedFirstRunFails_ReturnsDegraded()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 5, 26, 10, 0, 0, TimeSpan.Zero));
        var result = await CheckAsync(
            timeProvider,
            cacheNames: null,
            maxAge: null,
            Status(
                "hourly",
                successCount: 0,
                consecutiveFailures: 1,
                nextScheduledRunAt: timeProvider.GetUtcNow().AddHours(1)));

        result.Status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenWarmButLastRefreshFailed_ReturnsDegraded()
    {
        var result = await CheckAsync(
            Status("warm", successCount: 3, consecutiveFailures: 2));

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("warm");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenLastSuccessExceedsMaximumAge_ReturnsDegraded()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 5, 26, 10, 0, 0, TimeSpan.Zero));
        var result = await CheckAsync(
            timeProvider,
            cacheNames: null,
            maxAge: TimeSpan.FromMinutes(5),
            Status(
                "stale",
                successCount: 1,
                consecutiveFailures: 0,
                lastSuccessAt: timeProvider.GetUtcNow().AddMinutes(-6)));

        result.Status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenScopedToCacheNames_IgnoresOtherCaches()
    {
        var timeProvider = new FakeTimeProvider();
        var result = await CheckAsync(
            timeProvider,
            new HashSet<string>(["critical"], StringComparer.Ordinal),
            maxAge: null,
            Status("critical", successCount: 1, consecutiveFailures: 0),
            Status("best-effort", successCount: 1, consecutiveFailures: 3));

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data.Should().ContainKey("critical").And.NotContainKey("best-effort");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenAllWarmAndHealthy_ReturnsHealthy()
    {
        var result = await CheckAsync(
            Status("a", successCount: 5, consecutiveFailures: 0),
            Status("b", successCount: 1, consecutiveFailures: 0));

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data.Should().ContainKeys("a", "b");
    }

    private static Task<HealthCheckResult> CheckAsync(params RefreshAheadMemoryCacheStatus[] statuses) =>
        CheckAsync(TimeProvider.System, null, null, statuses);

    private static async Task<HealthCheckResult> CheckAsync(
        TimeProvider timeProvider,
        IReadOnlySet<string>? cacheNames,
        TimeSpan? maxAge,
        params RefreshAheadMemoryCacheStatus[] statuses)
    {
        var healthCheck = new RefreshAheadMemoryCacheHealthCheck(
            new FakeStatusProvider(statuses),
            timeProvider,
            cacheNames,
            maxAge);
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("test", healthCheck, failureStatus: null, tags: null),
        };

        return await healthCheck.CheckHealthAsync(context, CancellationToken.None);
    }

    private static RefreshAheadMemoryCacheStatus Status(
        string name,
        long successCount,
        long consecutiveFailures,
        DateTimeOffset? nextScheduledRunAt = null,
        DateTimeOffset? lastSuccessAt = null,
        bool isRefreshLoopFaulted = false,
        bool runImmediately = false) =>
        new()
        {
            Name = name,
            RunImmediately = runImmediately,
            SuccessCount = successCount,
            ConsecutiveFailures = consecutiveFailures,
            NextScheduledRunAt = nextScheduledRunAt,
            LastSuccessAt = lastSuccessAt,
            IsRefreshLoopFaulted = isRefreshLoopFaulted,
        };

    private sealed class FakeStatusProvider(IReadOnlyList<RefreshAheadMemoryCacheStatus> statuses)
        : IRefreshAheadMemoryCacheStatusProvider
    {
        public IReadOnlyList<RefreshAheadMemoryCacheStatus> GetStatuses() => statuses;

        public bool TryGetStatus(string name, [MaybeNullWhen(false)] out RefreshAheadMemoryCacheStatus status)
        {
            status = statuses.FirstOrDefault(s => s.Name == name);
            return status is not null;
        }
    }
}
