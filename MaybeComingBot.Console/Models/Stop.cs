namespace MaybeComingBot.Models;

public sealed class Stop
{
    private string _id = string.Empty;
    private string _name = string.Empty;
    private List<DaySchedule>? _schedule;

    public Stop() { }

    public Stop(string id, string name)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    public string Id
    {
        get => _id;
        set => _id = value ?? string.Empty;
    }

    public string Name
    {
        get => _name;
        set => _name = value ?? string.Empty;
    }

    public List<DaySchedule> Schedule
    {
        get => _schedule ??= [];
        set => _schedule = value ?? [];
    }

    public DaySchedule? GetScheduleForDay(int day)
    {
        return Schedule.FirstOrDefault(s => s.Day == day);
    }

    public bool HasTripsOnDay(int day)
    {
        return Schedule.Any(s => s.Day == day && s.Trips.Count > 0);
    }

    public void AddSchedule(DaySchedule daySchedule)
    {
        ArgumentNullException.ThrowIfNull(daySchedule);

        Schedule.Add(daySchedule);
    }

    public override string ToString()
    {
        return $"{Name} ({Id})";
    }
}