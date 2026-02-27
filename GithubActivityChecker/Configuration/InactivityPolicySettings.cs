namespace GithubActivityChecker.Configuration;

public class InactivityPolicySettings
{
    public const string SectionName = "InactivityPolicy";

    public int InactiveDays { get; set; } = 30;
    public int PendingRemovalDays { get; set; } = 60;
}
