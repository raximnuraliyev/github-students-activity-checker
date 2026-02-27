using GithubActivityChecker.Services;
using Microsoft.Extensions.Logging;
using Quartz;

namespace GithubActivityChecker.Jobs;

/// <summary>
/// Quartz.NET job that triggers the nightly full sync of GitHub activity.
/// </summary>
[DisallowConcurrentExecution]
public class FullSyncJob : IJob
{
    private readonly ISyncService _syncService;
    private readonly ILogger<FullSyncJob> _logger;

    public FullSyncJob(ISyncService syncService, ILogger<FullSyncJob> logger)
    {
        _syncService = syncService;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("FullSyncJob triggered at {Time}", DateTimeOffset.UtcNow);

        try
        {
            await _syncService.RunFullSyncAsync(context.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FullSyncJob encountered an unhandled error");
            throw new JobExecutionException(ex, refireImmediately: false);
        }
    }
}
