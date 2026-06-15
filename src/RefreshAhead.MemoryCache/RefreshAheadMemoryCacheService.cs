using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RefreshAhead.MemoryCache;

internal sealed partial class RefreshAheadMemoryCacheService(
    IServiceProvider serviceProvider,
    IEnumerable<IRefreshAheadMemoryCacheJob> jobs,
    TimeProvider timeProvider,
    RefreshAheadMemoryCacheStatusRegistry statusRegistry,
    IOptions<RefreshAheadMemoryCacheOptions> options,
    ILogger<RefreshAheadMemoryCacheService> logger) : BackgroundService, IHostedLifecycleService
{
    private readonly JobRuntime[] _runtimes = jobs
        .Select(job => new JobRuntime(
            job,
            statusRegistry.GetOrAdd(job.Name),
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)))
        .ToArray();

    private readonly RefreshAheadMemoryCacheOptions _options = options.Value;

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_runtimes.Length == 0)
        {
            return Task.CompletedTask;
        }

        var runners = _runtimes.Select(runtime => RunJobAsync(runtime, stoppingToken)).ToArray();
        return Task.WhenAll(runners);
    }

    Task IHostedLifecycleService.StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Runs after <see cref="BackgroundService.StartAsync"/> has started every refresh loop. Blocks host startup
    /// until every cache marked <see cref="IRefreshAheadMemoryCache{TSnapshot}.WarmUpOnStart"/> has completed its
    /// first successful refresh, bounded by <see cref="RefreshAheadMemoryCacheOptions.WarmUpTimeout"/>.
    /// </summary>
    async Task IHostedLifecycleService.StartedAsync(CancellationToken cancellationToken)
    {
        var warmUpTasks = _runtimes
            .Where(static runtime => runtime.Job.WarmUpOnStart)
            .Select(static runtime => runtime.WarmUp.Task)
            .ToArray();

        if (warmUpTasks.Length == 0)
        {
            return;
        }

        LogWarmUpWaiting(logger, warmUpTasks.Length);

        using var timeoutSource = _options.WarmUpTimeout is { } timeout
            ? new CancellationTokenSource(timeout, timeProvider)
            : new CancellationTokenSource();
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        try
        {
            await Task.WhenAll(warmUpTasks).WaitAsync(linkedSource.Token);
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Refresh-ahead memory cache warm-up did not complete within the configured WarmUpTimeout of {_options.WarmUpTimeout}.");
        }

        LogWarmUpCompleted(logger);
    }

    Task IHostedLifecycleService.StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    Task IHostedLifecycleService.StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task RunJobAsync(JobRuntime runtime, CancellationToken stoppingToken)
    {
        var job = runtime.Job;
        LogJobStarted(logger, job.Name);

        try
        {
            DateTimeOffset? previousScheduledRun = null;

            // Warm-up forces an eager first refresh even when RunImmediately is false, so the host can gate on it.
            if (!job.RunImmediately && !job.WarmUpOnStart)
            {
                previousScheduledRun = job.Schedule.GetNextRun(timeProvider.GetUtcNow(), null, null);
                await DelayUntilAsync(previousScheduledRun.Value, stoppingToken);
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                previousScheduledRun ??= timeProvider.GetUtcNow();

                DateTimeOffset? previousCompletedRun;
                try
                {
                    LogRefreshStarted(logger, job.Name);
                    runtime.Status.RecordStart(timeProvider.GetUtcNow());
                    await job.RefreshAsync(serviceProvider, stoppingToken);
                    previousCompletedRun = timeProvider.GetUtcNow();
                    runtime.Status.RecordSuccess(previousCompletedRun.Value);
                    runtime.WarmUp.TrySetResult();
                    LogRefreshCompleted(logger, job.Name);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    previousCompletedRun = timeProvider.GetUtcNow();
                    runtime.Status.RecordFailure(previousCompletedRun.Value, ex);
                    LogRefreshFailed(logger, ex, job.Name);
                }

                var nextRun = job.Schedule.GetNextRun(
                    timeProvider.GetUtcNow(),
                    previousScheduledRun,
                    previousCompletedRun);

                previousScheduledRun = nextRun;
                await DelayUntilAsync(nextRun, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            // Unblock the warm-up gate on shutdown so a host stopping mid warm-up never hangs.
            runtime.WarmUp.TrySetResult();
            LogJobStopped(logger, job.Name);
        }
    }

    private async Task DelayUntilAsync(DateTimeOffset nextRun, CancellationToken cancellationToken)
    {
        var delay = nextRun - timeProvider.GetUtcNow();
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, timeProvider, cancellationToken);
        }
    }

    private sealed record JobRuntime(
        IRefreshAheadMemoryCacheJob Job,
        RefreshAheadMemoryCacheStatusRegistry.StatusEntry Status,
        TaskCompletionSource WarmUp);

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Refresh-ahead memory cache job {JobName} started.")]
    private static partial void LogJobStarted(ILogger logger, string jobName);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Refresh-ahead memory cache job {JobName} stopped.")]
    private static partial void LogJobStopped(ILogger logger, string jobName);

    [LoggerMessage(EventId = 3, Level = LogLevel.Debug, Message = "Refreshing memory cache job {JobName}.")]
    private static partial void LogRefreshStarted(ILogger logger, string jobName);

    [LoggerMessage(EventId = 4, Level = LogLevel.Debug, Message = "Refreshed memory cache job {JobName}.")]
    private static partial void LogRefreshCompleted(ILogger logger, string jobName);

    [LoggerMessage(EventId = 5, Level = LogLevel.Error, Message = "Refresh-ahead memory cache job {JobName} failed.")]
    private static partial void LogRefreshFailed(ILogger logger, Exception exception, string jobName);

    [LoggerMessage(EventId = 6, Level = LogLevel.Information, Message = "Waiting for {WarmUpJobCount} refresh-ahead memory cache job(s) to warm up before completing host startup.")]
    private static partial void LogWarmUpWaiting(ILogger logger, int warmUpJobCount);

    [LoggerMessage(EventId = 7, Level = LogLevel.Information, Message = "Refresh-ahead memory cache warm-up completed.")]
    private static partial void LogWarmUpCompleted(ILogger logger);
}
