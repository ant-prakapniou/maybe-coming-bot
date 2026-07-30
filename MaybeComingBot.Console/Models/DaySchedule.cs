namespace MaybeComingBot.Models;

public sealed class DaySchedule
{
    public int Day { get; set; }
    public List<Trip> Trips { get; set; } = [];
}