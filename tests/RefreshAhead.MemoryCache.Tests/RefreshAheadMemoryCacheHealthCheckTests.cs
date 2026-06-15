using System.Diagnostics.CodeAnalysis;
using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

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
    public async Task CheckHealthAsync_WhenWarmButLastRefreshFailed_ReturnsDegraded()
    {
        var result = await CheckAsync(
            Status("warm", successCount: 3, consecutiveFailures: 2));

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("warm");
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

    private static async Task<HealthCheckResult> CheckAsync(params RefreshAheadMemoryCacheStatus[] statuses)
    {
        var healthCheck = new RefreshAheadMemoryCacheHealthCheck(new FakeStatusProvider(statuses));
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("test", healthCheck, failureStatus: null, tags: null),
        };

        return await healthCheck.CheckHealthAsync(context, CancellationToken.None);
    }

    private static RefreshAheadMemoryCacheStatus Status(string name, long successCount, long consecutiveFailures) =>
        new()
        {
            Name = name,
            SuccessCount = successCount,
            ConsecutiveFailures = consecutiveFailures,
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
