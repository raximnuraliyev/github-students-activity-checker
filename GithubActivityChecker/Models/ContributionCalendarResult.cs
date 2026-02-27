namespace GithubActivityChecker.Models;

/// <summary>
/// Represents the GitHub contribution calendar data fetched via GraphQL.
/// </summary>
public class ContributionCalendarResult
{
    public int TotalContributions { get; set; }
    public List<ContributionDay> Days { get; set; } = new();
}

public class ContributionDay
{
    public DateOnly Date { get; set; }
    public int ContributionCount { get; set; }
}
