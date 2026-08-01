namespace MaybeComingBot.Models;

/// <summary>
/// Represents the root container for the entire schedule data.
/// </summary>
/// <remarks>
/// Contains a collection of stops with their respective schedules.
/// Provides methods for querying, validation, and data inspection.
/// This is the top-level model used for JSON deserialization from the schedule URL.
/// </remarks>
public sealed class ScheduleRoot
{
    private List<Stop>? _stops;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScheduleRoot"/> class.
    /// </summary>
    public ScheduleRoot() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="ScheduleRoot"/> class with specified stops.
    /// </summary>
    /// <param name="stops">The collection of stops. Can be null.</param>
    public ScheduleRoot(List<Stop>? stops)
    {
        Stops = stops ?? [];
    }

    /// <summary>
    /// Gets or sets the collection of stops in the schedule.
    /// </summary>
    /// <value>
    /// A list of <see cref="Stop"/> objects. Never returns null.
    /// </value>
    public List<Stop> Stops
    {
        get => _stops ??= [];
        set => _stops = value ?? [];
    }

    /// <summary>
    /// Gets the total number of stops in the schedule.
    /// </summary>
    /// <value>
    /// The count of stops in the <see cref="Stops"/> collection.
    /// </value>
    public int StopCount => Stops.Count;

    /// <summary>
    /// Determines whether the schedule is empty or contains no trip data.
    /// </summary>
    /// <returns>
    /// <c>true</c> if there are no stops, or all stops have no trips; otherwise, <c>false</c>.
    /// </returns>
    public bool IsEmpty() => Stops.Count == 0 || Stops.All(s => s.Schedule.Count == 0);

    /// <summary>
    /// Retrieves a stop by its unique identifier.
    /// </summary>
    /// <param name="id">The stop identifier. Can be null or empty.</param>
    /// <returns>
    /// The <see cref="Stop"/> if found; otherwise, <c>null</c>.
    /// </returns>
    public Stop? GetStopById(string id)
    {
        if (string.IsNullOrEmpty(id))
            return null;

        return Stops.FirstOrDefault(s => s.Id == id);
    }

    /// <summary>
    /// Retrieves a stop by its name with case-insensitive comparison.
    /// </summary>
    /// <param name="name">The stop name. Can be null or empty.</param>
    /// <returns>
    /// The <see cref="Stop"/> if found; otherwise, <c>null</c>.
    /// </returns>
    public Stop? GetStopByName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return null;

        return Stops.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets a list of stops that have at least one trip scheduled.
    /// </summary>
    /// <returns>
    /// A list of <see cref="Stop"/> objects that contain trips.
    /// </returns>
    public List<Stop> GetStopsWithTrips()
    {
        return Stops.Where(s => s.Schedule.Any(d => d.Trips.Count > 0)).ToList();
    }

    /// <summary>
    /// Checks for duplicate stop IDs in the schedule.
    /// </summary>
    /// <returns>
    /// <c>true</c> if there are multiple stops with the same ID; otherwise, <c>false</c>.
    /// </returns>
    /// <remarks>
    /// Stop IDs must be unique for proper lookup operations.
    /// Duplicate IDs can cause data inconsistency issues.
    /// </remarks>
    public bool HasDuplicateStopIds()
    {
        return Stops.GroupBy(s => s.Id).Any(g => g.Count() > 1);
    }

    /// <summary>
    /// Checks for duplicate stop names in the schedule using case-insensitive comparison.
    /// </summary>
    /// <returns>
    /// <c>true</c> if there are multiple stops with the same name; otherwise, <c>false</c>.
    /// </returns>
    /// <remarks>
    /// While duplicate names are not critical, they can cause user confusion.
    /// This method helps identify potential display issues.
    /// </remarks>
    public bool HasDuplicateStopNames()
    {
        return Stops.GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                    .Any(g => g.Count() > 1);
    }

    /// <summary>
    /// Returns a string representation of the schedule root.
    /// </summary>
    /// <returns>
    /// A string containing the total number of stops (e.g., "ScheduleRoot: 15 stops").
    /// </returns>
    public override string ToString()
    {
        return $"ScheduleRoot: {StopCount} stops";
    }
}