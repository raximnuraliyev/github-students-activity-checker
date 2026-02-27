namespace GithubActivityChecker.Configuration;

public class SyncScheduleSettings
{
    public const string SectionName = "SyncSchedule";

    /// <summary>
    /// Quartz cron expression. Default: "0 0 2 * * ?" (every day at 02:00 AM).
    /// </summary>
    public string CronExpression { get; set; } = "0 0 2 * * ?";
}
