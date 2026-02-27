using GithubActivityChecker.Models;

namespace GithubActivityChecker.Services;

public interface IGitHubService
{
    /// <summary>
    /// Fetches the contribution calendar for a GitHub user for the last year.
    /// </summary>
    Task<ContributionCalendarResult?> GetContributionCalendarAsync(string githubUsername, CancellationToken ct = default);
}
