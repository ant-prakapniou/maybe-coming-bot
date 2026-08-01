namespace MaybeComingBot.Models;

public sealed class ScheduleRoot
{
    private List<Stop>? _stops;

    public ScheduleRoot() { }

    public ScheduleRoot(List<Stop>? stops)
    {
        Stops = stops ?? [];
    }

    public List<Stop> Stops
    {
        get => _stops ??= [];
        set => _stops = value ?? [];
    }

    public int StopCount => Stops.Count;

    public bool IsEmpty() => Stops.Count == 0 || Stops.All(s => s.Schedule.Count == 0);

    public Stop? GetStopById(string id)
    {
        if (string.IsNullOrEmpty(id))
            return null;

        return Stops.FirstOrDefault(s => s.Id == id);
    }

    public Stop? GetStopByName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return null;

        return Stops.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    public List<Stop> GetStopsWithTrips()
    {
        return Stops.Where(s => s.Schedule.Any(d => d.Trips.Count > 0)).ToList();
    }

    public bool HasDuplicateStopIds()
    {
        return Stops.GroupBy(s => s.Id).Any(g => g.Count() > 1);
    }

    public bool HasDuplicateStopNames()
    {
        return Stops.GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                    .Any(g => g.Count() > 1);
    }

    public override string ToString()
    {
        return $"ScheduleRoot: {StopCount} stops";
    }
}