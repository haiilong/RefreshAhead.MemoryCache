using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RefreshAhead.MemoryCache;

internal sealed partial class RefreshAheadMemoryCacheService(
    IServiceProvider serviceProvider,
    IEnumerable<IRefreshAheadMemoryCacheJob> jobs,
    TimeProvider timeProvider,
    ILogger<RefreshAheadMemoryCacheService> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var runners = jobs.Select(job => RunJobAsync(job, stoppingToken)).ToArray();
        return runners.Length == 0 ? Task.CompletedTask : Task.WhenAll(runners);
    }

    private async Task RunJobAsync(IRefreshAheadMemoryCacheJob job, CancellationToken stoppingToken)
    {
        LogJobStarted(logger, job.Name);

        try
        {
            DateTimeOffset? previousScheduledRun = null;

            if (!job.RunImmediately)
            {
                previousScheduledRun = job.Schedule.GetNextRun(timeProvider.GetUtcNow(), null, null);
                await DelayUntilAsync(previousScheduledRun.Value, stoppingToken);
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                var scheduledRun = previousScheduledRun ?? timeProvider.GetUtcNow();
                previousScheduledRun = scheduledRun;

                DateTimeOffset? previousCompletedRun;
                try
                {
                    LogRefreshStarted(logger, job.Name);
                    await job.RefreshAsync(serviceProvider, stoppingToken);
                    previousCompletedRun = timeProvider.GetUtcNow();
                    LogRefreshCompleted(logger, job.Name);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    previousCompletedRun = timeProvider.GetUtcNow();
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
}
