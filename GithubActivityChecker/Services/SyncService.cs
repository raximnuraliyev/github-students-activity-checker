using GithubActivityChecker.Configuration;
using GithubActivityChecker.Data;
using GithubActivityChecker.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GithubActivityChecker.Services;

public class SyncService : ISyncService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IGitHubService _gitHubService;
    private readonly GitHubSettings _gitHubSettings;
    private readonly InactivityPolicySettings _policySettings;
    private readonly ILogger<SyncService> _logger;

    public SyncService(
        IServiceScopeFactory scopeFactory,
        IGitHubService gitHubService,
        IOptions<GitHubSettings> gitHubSettings,
        IOptions<InactivityPolicySettings> policySettings,
        ILogger<SyncService> logger)
    {
        _scopeFactory = scopeFactory;
        _gitHubService = gitHubService;
        _gitHubSettings = gitHubSettings.Value;
        _policySettings = policySettings.Value;
        _logger = logger;
    }

    public async Task RunFullSyncAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Starting full GitHub activity sync...");
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var students = await db.Students
            .OrderBy(s => s.GithubUsername)
            .ToListAsync(ct);

        _logger.LogInformation("Found {Count} students to sync", students.Count);

        int batchSize = _gitHubSettings.BatchSize;
        int processed = 0;
        int failed = 0;

        for (int i = 0; i < students.Count; i += batchSize)
        {
            if (ct.IsCancellationRequested) break;

            var batch = students.Skip(i).Take(batchSize).ToList();
            _logger.LogInformation("Processing batch {BatchStart}-{BatchEnd} of {Total}",
                i + 1, Math.Min(i + batchSize, students.Count), students.Count);

            foreach (var student in batch)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    var calendar = await _gitHubService.GetContributionCalendarAsync(student.GithubUsername, ct);
                    if (calendar is null)
                    {
                        _logger.LogWarning("Skipping {Username} — no data returned", student.GithubUsername);
                        failed++;
                        continue;
                    }

                    // Upsert daily contributions
                    foreach (var day in calendar.Days)
                    {
                        var existing = await db.DailyContributions
                            .FirstOrDefaultAsync(dc => dc.StudentId == student.Id && dc.Date == day.Date, ct);

                        if (existing is not null)
                        {
                            existing.Count = day.ContributionCount;
                        }
                        else
                        {
                            db.DailyContributions.Add(new DailyContribution
                            {
                                StudentId = student.Id,
                                Date = day.Date,
                                Count = day.ContributionCount
                            });
                        }
                    }

                    // Determine last active date from contributions
                    var lastActive = calendar.Days
                        .Where(d => d.ContributionCount > 0)
                        .OrderByDescending(d => d.Date)
                        .FirstOrDefault();

                    student.LastActiveDate = lastActive is not null
                        ? lastActive.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
                        : student.LastActiveDate;

                    // Apply inactivity policy
                    var today = DateOnly.FromDateTime(DateTime.UtcNow);
                    int last30 = calendar.Days
                        .Where(d => d.Date >= today.AddDays(-_policySettings.InactiveDays))
                        .Sum(d => d.ContributionCount);

                    int last60 = calendar.Days
                        .Where(d => d.Date >= today.AddDays(-_policySettings.PendingRemovalDays))
                        .Sum(d => d.ContributionCount);

                    if (last60 == 0)
                        student.Status = StudentStatus.Pending_Removal;
                    else if (last30 == 0)
                        student.Status = StudentStatus.Inactive;
                    else
                        student.Status = StudentStatus.Active;

                    student.UpdatedAt = DateTime.UtcNow;
                    processed++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error syncing student {Username}", student.GithubUsername);
                    failed++;
                }
            }

            await db.SaveChangesAsync(ct);

            // Rate-limit delay between batches
            if (i + batchSize < students.Count)
            {
                _logger.LogDebug("Waiting {Delay}ms before next batch...", _gitHubSettings.DelayBetweenBatchesMs);
                await Task.Delay(_gitHubSettings.DelayBetweenBatchesMs, ct);
            }
        }

        stopwatch.Stop();
        _logger.LogInformation(
            "Full sync completed in {Elapsed}. Processed: {Processed}, Failed: {Failed}",
            stopwatch.Elapsed, processed, failed);
    }
}
