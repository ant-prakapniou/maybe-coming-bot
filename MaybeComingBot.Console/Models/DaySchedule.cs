namespace MaybeComingBot.Models;

/// <summary>
/// Represents the schedule for a single day of the week at a specific stop.
/// </summary>
/// <remarks>
/// Contains a collection of trips for a given day (0 = Sunday, 6 = Saturday).
/// Provides methods for sorting, validation, and duplicate detection.
/// </remarks>
public sealed class DaySchedule
{
    private int _day;
    private List<Trip>? _trips;

    /// <summary>
    /// Initializes a new instance of the <see cref="DaySchedule"/> class.
    /// </summary>
    public DaySchedule() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="DaySchedule"/> class with specified day and trips.
    /// </summary>
    /// <param name="day">The day of the week (0 = Sunday, 6 = Saturday).</param>
    /// <param name="trips">The collection of trips for this day. Can be null.</param>
    public DaySchedule(int day, List<Trip>? trips = null)
    {
        Day = day;
        Trips = trips ?? [];
    }

    /// <summary>
    /// Gets or sets the day of the week as an integer.
    /// </summary>
    /// <value>
    /// An integer between 0 (Sunday) and 6 (Saturday).
    /// Invalid values are automatically clamped to 0.
    /// </value>
    public int Day
    {
        get => _day;
        set => _day = value >= 0 && value <= 6 ? value : 0;
    }

    /// <summary>
    /// Gets or sets the collection of trips for this day.
    /// </summary>
    /// <value>
    /// A list of <see cref="Trip"/> objects. Never returns null.
    /// </value>
    public List<Trip> Trips
    {
        get => _trips ??= [];
        set => _trips = value ?? [];
    }

    /// <summary>
    /// Gets the day of the week as a <see cref="DayOfWeek"/> enumeration.
    /// </summary>
    /// <value>
    /// The <see cref="DayOfWeek"/> representation of the current day.
    /// </value>
    public DayOfWeek DayOfWeek => (DayOfWeek)Day;

    /// <summary>
    /// Determines whether this day has any trips.
    /// </summary>
    /// <returns>
    /// <c>true</c> if the trip collection contains at least one trip; otherwise, <c>false</c>.
    /// </returns>
    public bool HasTrips() => Trips.Count > 0;

    /// <summary>
    /// Adds a trip to the day's schedule.
    /// </summary>
    /// <param name="trip">The trip to add. Cannot be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="trip"/> is null.</exception>
    public void AddTrip(Trip trip)
    {
        if (trip is null)
            throw new ArgumentNullException(nameof(trip));

        Trips.Add(trip);
    }

    /// <summary>
    /// Gets a read-only list of trips sorted by departure time.
    /// </summary>
    /// <returns>
    /// A read-only list of valid trips sorted in ascending order by time.
    /// </returns>
    /// <remarks>
    /// Invalid trips (those with missing or invalid time) are filtered out.
    /// Uses <see cref="Trip.ToMinutesSafe"/> for time comparison.
    /// </remarks>
    public IReadOnlyList<Trip> GetSortedTrips()
    {
        return Trips
            .Where(t => t.IsValid())
            .OrderBy(t => t.ToMinutesSafe())
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Checks for duplicate trips based on departure time.
    /// </summary>
    /// <returns>
    /// <c>true</c> if there are multiple trips with the same departure time; otherwise, <c>false</c>.
    /// </returns>
    public bool HasDuplicateTrips()
    {
        return Trips.GroupBy(t => t.Time).Any(g => g.Count() > 1);
    }

    /// <summary>
    /// Returns a string representation of the day schedule.
    /// </summary>
    /// <returns>
    /// A string containing the day name and the number of trips (e.g., "Monday: 12 trips").
    /// </returns>
    public override string ToString()
    {
        return $"{DayOfWeek}: {Trips.Count} trips";
    }
}