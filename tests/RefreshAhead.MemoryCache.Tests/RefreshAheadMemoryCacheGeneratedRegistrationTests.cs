using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace RefreshAhead.MemoryCache.Tests;

public sealed class RefreshAheadMemoryCacheGeneratedRegistrationTests
{
    [Fact]
    public async Task AddRefreshAheadMemoryCaches_RegistersDiscoveredCachesAndRunsRefresh()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 5, 26, 10, 0, 0, TimeSpan.Zero));
        var services = new ServiceCollection();

        services.AddSingleton<TimeProvider>(timeProvider);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddRefreshAheadMemoryCaches();

        await using var serviceProvider = services.BuildServiceProvider();
        var cache = serviceProvider.GetRequiredService<GeneratedRefreshAheadCache>();
        var hostedService = serviceProvider.GetServices<IHostedService>().Should().ContainSingle().Which;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        await hostedService.StartAsync(CancellationToken.None);
        await cache.WaitForCountAsync(1, timeout.Token);
        await hostedService.StopAsync(CancellationToken.None);

        // Assert
        cache.Snapshot.Should().Be(1);
        serviceProvider
            .GetRequiredService<IRefreshAheadMemoryCache<int>>()
            .Should()
            .BeSameAs(cache);
    }

    [Fact]
    public async Task RefreshAheadMemoryCacheBase_PublishesReturnedSnapshot()
    {
        // Arrange
        var cache = new GeneratedBaseClassCache();

        // Act
        await cache.RefreshCacheAsync(CancellationToken.None);

        // Assert
        cache.Snapshot.Should().Equal(1, 2, 3);
        cache.PublishCount.Should().Be(1);
    }
}

internal sealed class GeneratedRefreshAheadCache : IRefreshAheadMemoryCache<int>
{
    private int _refreshCount;

    public RefreshAheadSchedule Schedule => RefreshAheadSchedule.FixedDelay(TimeSpan.FromSeconds(1));

    public int Snapshot => _refreshCount;

    public ValueTask RefreshCacheAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _refreshCount);
        return ValueTask.CompletedTask;
    }

    public async Task WaitForCountAsync(int count, CancellationToken cancellationToken)
    {
        while (Volatile.Read(ref _refreshCount) < count)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
        }
    }
}

internal sealed class GeneratedBaseClassCache()
    : RefreshAheadMemoryCache<IReadOnlyList<int>>([])
{
    public int PublishCount { get; private set; }

    public override RefreshAheadSchedule Schedule => RefreshAheadSchedule.FixedDelay(TimeSpan.FromSeconds(1));

    protected override ValueTask<IReadOnlyList<int>> RefreshSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IReadOnlyList<int>>([1, 2, 3]);
    }

    protected override void PublishSnapshot(IReadOnlyList<int> snapshot)
    {
        base.PublishSnapshot(snapshot);
        PublishCount++;
    }
}
