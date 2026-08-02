namespace MaybeComingBot.Models;

/// <summary>
/// Represents a public transport stop with its schedule information.
/// </summary>
/// <remarks>
/// Contains basic stop metadata (ID, Name) and a collection of day schedules.
/// Provides methods for querying specific days and managing schedule data.
/// </remarks>
public sealed class Stop
{
    private string _id = string.Empty;
    private string _name = string.Empty;
    private List<DaySchedule>? _schedule;

    /// <summary>
    /// Initializes a new instance of the <see cref="Stop"/> class.
    /// </summary>
    public Stop() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="Stop"/> class with specified ID and name.
    /// </summary>
    /// <param name="id">The unique identifier for the stop. Cannot be null.</param>
    /// <param name="name">The display name of the stop. Cannot be null.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="id"/> or <paramref name="name"/> is null.
    /// </exception>
    public Stop(string id, string name)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    /// <summary>
    /// Gets or sets the unique identifier of the stop.
    /// </summary>
    /// <value>
    /// A string containing the stop ID. Never returns null.
    /// </value>
    /// <remarks>
    /// This ID is used as the primary key for lookups and must be unique across all stops.
    /// </remarks>
    public string Id
    {
        get => _id;
        set => _id = value ?? string.Empty;
    }

    /// <summary>
    /// Gets or sets the display name of the stop.
    /// </summary>
    /// <value>
    /// A string containing the stop name. Never returns null.
    /// </value>
    /// <remarks>
    /// This name is shown to users when selecting stops and in schedule displays.
    /// Names should be descriptive and user-friendly.
    /// </remarks>
    public string Name
    {
        get => _name;
        set => _name = value ?? string.Empty;
    }

    /// <summary>
    /// Gets or sets the collection of day schedules for this stop.
    /// </summary>
    /// <value>
    /// A list of <see cref="DaySchedule"/> objects. Never returns null.
    /// </value>
    public List<DaySchedule> Schedule
    {
        get => _schedule ??= [];
        set => _schedule = value ?? [];
    }

    /// <summary>
    /// Retrieves the schedule for a specific day of the week.
    /// </summary>
    /// <param name="day">The day of the week (0 = Sunday, 6 = Saturday).</param>
    /// <returns>
    /// The <see cref="DaySchedule"/> if found; otherwise, <c>null</c>.
    /// </returns>
    public DaySchedule? GetScheduleForDay(int day)
        => Schedule.FirstOrDefault(s => s.Day == day);

    /// <summary>
    /// Determines whether the stop has any trips scheduled on a specific day.
    /// </summary>
    /// <param name="day">The day of the week (0 = Sunday, 6 = Saturday).</param>
    /// <returns>
    /// <c>true</c> if there is at least one trip on the specified day; otherwise, <c>false</c>.
    /// </returns>
    public bool HasTripsOnDay(int day)
        => Schedule.Any(s => s.Day == day && s.Trips.Count > 0);

    /// <summary>
    /// Adds a day schedule to the stop.
    /// </summary>
    /// <param name="daySchedule">The day schedule to add. Cannot be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="daySchedule"/> is null.</exception>
    public void AddSchedule(DaySchedule daySchedule)
    {
        ArgumentNullException.ThrowIfNull(daySchedule);
        Schedule.Add(daySchedule);
    }

    /// <summary>
    /// Returns a string representation of the stop.
    /// </summary>
    /// <returns>
    /// A string containing the stop name and ID (e.g., "Central Station (ST001)").
    /// </returns>
    public override string ToString()
        => $"{Name} ({Id})";
}