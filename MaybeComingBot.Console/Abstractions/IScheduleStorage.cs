using MaybeComingBot.Models;

namespace MaybeComingBot.Abstractions;

public interface IScheduleStorage
{
    IEnumerable<Stop> GetAllStops();
    Stop? GetStop(string id);
    IReadOnlyList<Trip>? GetTripsForDay(string stopId, int day);
    void Update(ScheduleRoot newData);
    bool HasData();
    int StopCount { get; }
    int CachedTripsCount { get; }
    bool IsLoaded { get; }
}