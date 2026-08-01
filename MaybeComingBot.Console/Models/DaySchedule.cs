namespace MaybeComingBot.Models;

public sealed class DaySchedule
{
    private int _day;
    private List<Trip>? _trips;

    public DaySchedule() { }

    public DaySchedule(int day, List<Trip>? trips = null)
    {
        Day = day;
        Trips = trips ?? [];
    }

    public int Day
    {
        get => _day;
        set => _day = value >= 0 && value <= 6 ? value : 0;
    }

    public List<Trip> Trips
    {
        get => _trips ??= [];
        set => _trips = value ?? [];
    }

    public DayOfWeek DayOfWeek => (DayOfWeek)Day;

    public bool HasTrips() => Trips.Count > 0;

    public void AddTrip(Trip trip)
    {
        if (trip is null)
            throw new ArgumentNullException(nameof(trip));

        Trips.Add(trip);
    }

    public IReadOnlyList<Trip> GetSortedTrips()
    {
        return Trips
            .Where(t => t.IsValid())
            .OrderBy(t => t.ToMinutesSafe())
            .ToList()
            .AsReadOnly();
    }

    public bool HasDuplicateTrips()
    {
        return Trips.GroupBy(t => t.Time).Any(g => g.Count() > 1);
    }

    public override string ToString()
    {
        return $"{DayOfWeek}: {Trips.Count} trips";
    }
}