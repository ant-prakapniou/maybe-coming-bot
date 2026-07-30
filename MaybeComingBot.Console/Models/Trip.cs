namespace MaybeComingBot.Models;

public sealed class Trip
{
    public string Time { get; set; } = string.Empty;
    public int Bus { get; set; }
    public string Note { get; set; } = string.Empty;
}
