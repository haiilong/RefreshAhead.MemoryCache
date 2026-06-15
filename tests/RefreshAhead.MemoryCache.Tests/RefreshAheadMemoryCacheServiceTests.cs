using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace RefreshAhead.MemoryCache.Tests;

public sealed class RefreshAheadMemoryCacheServiceTests
{
    [Fact]
    public async Task StartAsync_RunsImmediateRefresh()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 5, 26, 10, 0, 0, TimeSpan.Zero));
        var cache = new CountingCache();
        await using var serviceProvider = CreateServiceProvider(cache);
        var job = new RefreshAheadMemoryCacheJob(
            "counting-cache",
            RefreshAheadSchedule.FixedDelay(TimeSpan.FromSeconds(1)),
            async (_, ct) => await cache.RefreshAsync(ct),
            runImmediately: true);
        var service = CreateService(serviceProvider, job, timeProvider);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        await service.StartAsync(CancellationToken.None);
        await cache.WaitForCountAsync(1, timeout.Token);
        await service.StopAsync(CancellationToken.None);

        // Assert
        cache.RefreshCount.Should().Be(1);
    }

    [Fact]
    public async Task RefreshLoop_DoesNotOverlapRefreshes()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 5, 26, 10, 0, 0, TimeSpan.Zero));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = new BlockingCache(gate.Task);
        await using var serviceProvider = CreateServiceProvider(cache);
        var job = new RefreshAheadMemoryCacheJob(
            "blocking-cache",
            RefreshAheadSchedule.FixedRate(TimeSpan.FromSeconds(1)),
            async (_, ct) => await cache.RefreshAsync(ct),
            runImmediately: true);
        var service = CreateService(serviceProvider, job, timeProvider);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        await service.StartAsync(CancellationToken.None);
        await cache.WaitForCountAsync(1, timeout.Token);
        timeProvider.Advance(TimeSpan.FromSeconds(10));

        // Assert
        cache.RefreshCount.Should().Be(1);
        cache.MaxConcurrentRefreshes.Should().Be(1);

        gate.SetResult();
        await service.StopAsync(CancellationToken.None);

        cache.MaxConcurrentRefreshes.Should().Be(1);
    }

    [Fact]
    public async Task StartAsync_WhenRunImmediatelyFalse_WaitsUntilFirstScheduledRun()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 5, 26, 10, 0, 0, TimeSpan.Zero));
        var cache = new CountingCache();
        await using var serviceProvider = CreateServiceProvider(cache);
        var job = new RefreshAheadMemoryCacheJob(
            "counting-cache",
            RefreshAheadSchedule.FixedDelay(TimeSpan.FromSeconds(1)),
            async (_, ct) => await cache.RefreshAsync(ct),
            runImmediately: false);
        var service = CreateService(serviceProvider, job, timeProvider);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        await service.StartAsync(CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromMilliseconds(999));

        // Assert
        cache.RefreshCount.Should().Be(0);

        timeProvider.Advance(TimeSpan.FromMilliseconds(1));
        await cache.WaitForCountAsync(1, timeout.Token);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task RefreshLoop_ContinuesAfterRefreshFailure()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 5, 26, 10, 0, 0, TimeSpan.Zero));
        var cache = new FailsOnceCache();
        await using var serviceProvider = CreateServiceProvider(cache);
        var job = new RefreshAheadMemoryCacheJob(
            "fails-once-cache",
            RefreshAheadSchedule.FixedDelay(TimeSpan.FromSeconds(1)),
            async (_, ct) => await cache.RefreshAsync(ct),
            runImmediately: true);
        var service = CreateService(serviceProvider, job, timeProvider);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        await service.StartAsync(CancellationToken.None);
        await cache.WaitForCountAsync(1, timeout.Token);
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        await cache.WaitForCountAsync(2, timeout.Token);
        await service.StopAsync(CancellationToken.None);

        // Assert
        cache.RefreshCount.Should().Be(2);
        cache.SuccessCount.Should().Be(1);
    }

    [Fact]
    public async Task AddRefreshAheadMemoryCaches_ResolvesServiceFromFreshScopeForEachRefresh()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 5, 26, 10, 0, 0, TimeSpan.Zero));
        var gate = new object();
        var instanceIds = new List<Guid>();
        var services = new ServiceCollection();

        services.AddSingleton<TimeProvider>(timeProvider);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddScoped<ScopedCache>();
        services.AddRefreshAheadMemoryCaches(refresh => refresh
            .Every<ScopedCache>(
                TimeSpan.FromSeconds(1),
                static (cache, _) =>
                {
                    cache.RecordRefresh();
                    return ValueTask.CompletedTask;
                }));
        services.AddSingleton(new RefreshRecorder(gate, instanceIds));

        await using var serviceProvider = services.BuildServiceProvider();
        var hostedService = serviceProvider.GetServices<IHostedService>().Should().ContainSingle().Which;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        await hostedService.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => HasRecordedRefreshes(gate, instanceIds, 1), timeout.Token);
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        await WaitUntilAsync(() => HasRecordedRefreshes(gate, instanceIds, 2), timeout.Token);
        await hostedService.StopAsync(CancellationToken.None);

        // Assert
        GetRecordedInstanceIds(gate, instanceIds).Should().HaveCount(2).And.OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task StartedAsync_WhenWarmUpOnStart_BlocksUntilFirstRefreshSucceeds()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 5, 26, 10, 0, 0, TimeSpan.Zero));
        var cache = new CountingCache();
        await using var serviceProvider = CreateServiceProvider(cache);
        var job = new RefreshAheadMemoryCacheJob(
            "counting-cache",
            RefreshAheadSchedule.DailyAt(new TimeOnly(3, 30), TimeZoneInfo.Utc),
            async (_, ct) => await cache.RefreshAsync(ct),
            runImmediately: false,
            warmUpOnStart: true);
        var service = CreateService(serviceProvider, job, timeProvider);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        await service.StartAsync(CancellationToken.None);
        await ((IHostedLifecycleService)service).StartedAsync(timeout.Token);

        // Assert: warm-up forces an eager first refresh even though RunImmediately is false,
        // and StartedAsync does not return until that refresh has succeeded.
        cache.RefreshCount.Should().BeGreaterThanOrEqualTo(1);

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartedAsync_WhenWarmUpTimesOut_Throws()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 5, 26, 10, 0, 0, TimeSpan.Zero));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = new BlockingCache(gate.Task);
        await using var serviceProvider = CreateServiceProvider(cache);
        var job = new RefreshAheadMemoryCacheJob(
            "blocking-cache",
            RefreshAheadSchedule.FixedDelay(TimeSpan.FromSeconds(1)),
            async (_, ct) => await cache.RefreshAsync(ct),
            runImmediately: true,
            warmUpOnStart: true);
        var service = CreateService(
            serviceProvider,
            job,
            timeProvider,
            options: new RefreshAheadMemoryCacheOptions { WarmUpTimeout = TimeSpan.FromSeconds(5) });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        await service.StartAsync(CancellationToken.None);
        var startedTask = ((IHostedLifecycleService)service).StartedAsync(CancellationToken.None);
        await cache.WaitForCountAsync(1, timeout.Token);
        timeProvider.Advance(TimeSpan.FromSeconds(5));

        // Assert
        var act = async () => await startedTask;
        await act.Should().ThrowAsync<TimeoutException>();

        gate.SetResult();
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StatusProvider_TracksSuccessesAndFailures()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 5, 26, 10, 0, 0, TimeSpan.Zero));
        var cache = new FailsOnceCache();
        await using var serviceProvider = CreateServiceProvider(cache);
        var registry = new RefreshAheadMemoryCacheStatusRegistry();
        var job = new RefreshAheadMemoryCacheJob(
            "fails-once-cache",
            RefreshAheadSchedule.FixedDelay(TimeSpan.FromSeconds(1)),
            async (_, ct) => await cache.RefreshAsync(ct),
            runImmediately: true);
        var service = CreateService(serviceProvider, job, timeProvider, registry);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        await service.StartAsync(CancellationToken.None);
        await cache.WaitForCountAsync(1, timeout.Token);
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        await cache.WaitForCountAsync(2, timeout.Token);
        await WaitUntilAsync(
            () => registry.TryGetStatus("fails-once-cache", out var s) && s.SuccessCount >= 1,
            timeout.Token);
        await service.StopAsync(CancellationToken.None);

        // Assert
        registry.TryGetStatus("fails-once-cache", out var status).Should().BeTrue();
        status!.Name.Should().Be("fails-once-cache");
        status.IsWarm.Should().BeTrue();
        status.FailureCount.Should().Be(1);
        status.SuccessCount.Should().Be(1);
        status.ConsecutiveFailures.Should().Be(0);
        status.LastError.Should().BeNull();
        status.LastFailureAt.Should().NotBeNull();
        status.LastSuccessAt.Should().NotBeNull();

        registry.GetStatuses().Should().ContainSingle().Which.Name.Should().Be("fails-once-cache");
    }

    private static RefreshAheadMemoryCacheService CreateService(
        IServiceProvider serviceProvider,
        IRefreshAheadMemoryCacheJob job,
        TimeProvider timeProvider,
        RefreshAheadMemoryCacheStatusRegistry? statusRegistry = null,
        RefreshAheadMemoryCacheOptions? options = null) =>
        new(
            serviceProvider,
            [job],
            timeProvider,
            statusRegistry ?? new RefreshAheadMemoryCacheStatusRegistry(),
            Options.Create(options ?? new RefreshAheadMemoryCacheOptions()),
            NullLogger<RefreshAheadMemoryCacheService>.Instance);

    private static ServiceProvider CreateServiceProvider<TCache>(TCache cache)
        where TCache : class
    {
        var services = new ServiceCollection();
        services.AddSingleton(cache);
        return services.BuildServiceProvider();
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, CancellationToken cancellationToken)
    {
        while (!predicate())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
        }
    }

    private static bool HasRecordedRefreshes(object gate, List<Guid> instanceIds, int count)
    {
        lock (gate)
        {
            return instanceIds.Count >= count;
        }
    }

    private static IReadOnlyList<Guid> GetRecordedInstanceIds(object gate, List<Guid> instanceIds)
    {
        lock (gate)
        {
            return [.. instanceIds];
        }
    }

    private sealed class CountingCache
    {
        private readonly TaskCompletionSource _refreshed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _refreshCount;

        public int RefreshCount => _refreshCount;

        public Task RefreshAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _refreshCount);
            _refreshed.TrySetResult();
            return Task.CompletedTask;
        }

        public async Task WaitForCountAsync(int count, CancellationToken cancellationToken)
        {
            while (Volatile.Read(ref _refreshCount) < count)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
            }
        }
    }

    private sealed class FailsOnceCache
    {
        private int _refreshCount;
        private int _successCount;

        public int RefreshCount => _refreshCount;

        public int SuccessCount => _successCount;

        public Task RefreshAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var refreshCount = Interlocked.Increment(ref _refreshCount);
            if (refreshCount == 1)
            {
                throw new InvalidOperationException("Planned test failure.");
            }

            Interlocked.Increment(ref _successCount);
            return Task.CompletedTask;
        }

        public async Task WaitForCountAsync(int count, CancellationToken cancellationToken)
        {
            while (Volatile.Read(ref _refreshCount) < count)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
            }
        }
    }

    private sealed class RefreshRecorder(object gate, List<Guid> instanceIds)
    {
        public void Record(Guid instanceId)
        {
            lock (gate)
            {
                instanceIds.Add(instanceId);
            }
        }
    }

    private sealed class ScopedCache(RefreshRecorder recorder)
    {
        private readonly Guid _instanceId = Guid.NewGuid();

        public void RecordRefresh() => recorder.Record(_instanceId);
    }

    private sealed class BlockingCache(Task refreshGate)
    {
        private readonly TaskCompletionSource _firstRefreshStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _concurrentRefreshes;
        private int _refreshCount;

        public int RefreshCount => _refreshCount;

        public int MaxConcurrentRefreshes { get; private set; }

        public async Task RefreshAsync(CancellationToken cancellationToken)
        {
            var concurrentRefreshes = Interlocked.Increment(ref _concurrentRefreshes);
            MaxConcurrentRefreshes = Math.Max(MaxConcurrentRefreshes, concurrentRefreshes);
            Interlocked.Increment(ref _refreshCount);
            _firstRefreshStarted.TrySetResult();

            try
            {
                await refreshGate.WaitAsync(cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref _concurrentRefreshes);
            }
        }

        public async Task WaitForCountAsync(int count, CancellationToken cancellationToken)
        {
            while (Volatile.Read(ref _refreshCount) < count)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
            }
        }
    }
}
