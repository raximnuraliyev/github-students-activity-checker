namespace GithubActivityChecker.Models;

public class Student
{
    public Guid Id { get; set; }
    public string UniversityId { get; set; } = string.Empty;
    public string GithubUsername { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTime? LastActiveDate { get; set; }
    public StudentStatus Status { get; set; } = StudentStatus.Active;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation property
    public ICollection<DailyContribution> DailyContributions { get; set; } = new List<DailyContribution>();
}
