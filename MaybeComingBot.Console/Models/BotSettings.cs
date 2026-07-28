namespace MaybeComingBot.Console.Models;

public class BotSettings
{
    public string BotToken { get; set; } = string.Empty;
    public string JsonUrl { get; set; } = string.Empty;
    public long AdminUserId { get; set; }
}
