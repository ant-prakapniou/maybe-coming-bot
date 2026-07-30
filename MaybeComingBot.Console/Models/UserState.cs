namespace MaybeComingBot.Models;

public class UserState
{
    public string SelectedStopId { get; set; } = string.Empty;
    public DateTime LastActivity { get; set; } = DateTime.UtcNow;
    public int CommandCount { get; set; }
    public DateTime? CommandResetTime { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
}