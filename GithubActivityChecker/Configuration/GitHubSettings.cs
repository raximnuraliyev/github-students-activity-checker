namespace GithubActivityChecker.Configuration;

public class GitHubSettings
{
    public const string SectionName = "GitHub";

    public string PersonalAccessToken { get; set; } = string.Empty;
    public int BatchSize { get; set; } = 50;
    public int DelayBetweenBatchesMs { get; set; } = 5000;
}
