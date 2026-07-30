namespace MaybeComingBot.Models;

public sealed class Stop
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<DaySchedule> Schedule { get; set; } = [];
}