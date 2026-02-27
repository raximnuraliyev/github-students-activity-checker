namespace GithubActivityChecker.Models;

public class DailyContribution
{
    public Guid StudentId { get; set; }
    public DateOnly Date { get; set; }
    public int Count { get; set; }

    // Navigation property
    public Student Student { get; set; } = null!;
}
