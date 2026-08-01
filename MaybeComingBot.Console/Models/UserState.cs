namespace MaybeComingBot.Models;

public sealed class UserState
{
    public string SelectedStopId { get; set; } = string.Empty;
    public DateTime LastActivity { get; set; } = DateTime.UtcNow;
    public int CommandCount { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;

    public void IncrementCommandCount()
    {
        CommandCount++;
        LastActivity = DateTime.UtcNow;
    }

    public void ResetCommandCount()
    {
        CommandCount = 0;
    }
}