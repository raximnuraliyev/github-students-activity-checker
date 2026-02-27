namespace GithubActivityChecker.Models;

public class BotUser
{
    public long ChatId { get; set; }
    public string? Username { get; set; }
    public BotUserRole Role { get; set; } = BotUserRole.Student;
    public string Language { get; set; } = "en";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
