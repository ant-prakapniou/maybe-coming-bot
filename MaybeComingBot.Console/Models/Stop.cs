namespace MaybeComingBot.Console.Models;

public sealed class Stop
{
    public string Id { get; set; }
    public string Name { get; set; }
    public List<DaySchedule> Schedule { get; set; }
}
