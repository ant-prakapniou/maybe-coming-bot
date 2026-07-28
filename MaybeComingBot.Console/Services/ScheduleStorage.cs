using MaybeComingBot.Console.Models;

namespace MaybeComingBot.Console.Services;

public class ScheduleStorage
{
    private ScheduleRoot _data = new() { Stops = [] };

    public IEnumerable<Stop> GetAllStops() => _data.Stops;

    public Stop? GetStop(string id) => _data.Stops.FirstOrDefault(s => s.Id == id);

    public List<Trip>? GetTripsForDay(string stopId, int day)
    {
        var stop = GetStop(stopId);
        return stop?.Schedule.FirstOrDefault(d => d.Day == day)?.Trips;
    }

    public void Update(ScheduleRoot newData)
    {
        _data = newData ?? new ScheduleRoot { Stops = [] };
    }
}
