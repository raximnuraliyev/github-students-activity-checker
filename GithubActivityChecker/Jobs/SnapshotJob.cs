using GithubActivityChecker.Services;
using Microsoft.Extensions.Logging;
using Quartz;

namespace GithubActivityChecker.Jobs;

/// <summary>
/// Quartz.NET job that regenerates all visualization snapshots after the daily sync.
/// Runs after FullSyncJob to pre-render charts for instant Telegram delivery.
/// </summary>
[DisallowConcurrentExecution]
public class SnapshotJob : IJob
{
    private readonly IPlotService _plotService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SnapshotJob> _logger;

    public SnapshotJob(
        IPlotService plotService,
        IServiceProvider serviceProvider,
        ILogger<SnapshotJob> logger)
    {
        _plotService = plotService;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("SnapshotJob triggered at {Time} — regenerating visualization cache", DateTimeOffset.UtcNow);

        try
        {
            await _plotService.GenerateSnapshotsAsync(_serviceProvider, context.CancellationToken);
            _logger.LogInformation("SnapshotJob completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SnapshotJob encountered an unhandled error");
            throw new JobExecutionException(ex, refireImmediately: false);
        }
    }
}
