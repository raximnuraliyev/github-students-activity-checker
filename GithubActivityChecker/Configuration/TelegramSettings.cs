namespace GithubActivityChecker.Configuration;

public class TelegramSettings
{
    public const string SectionName = "Telegram";

    public string BotToken { get; set; } = string.Empty;
    public long[] AuthorizedChatIds { get; set; } = [];

    /// <summary>
    /// Telegram username (without @) of the Head admin who can manage other admins.
    /// </summary>
    public string HeadUsername { get; set; } = "rahimnuraliev";
}
