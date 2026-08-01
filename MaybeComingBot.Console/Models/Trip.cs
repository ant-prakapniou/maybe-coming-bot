using MaybeComingBot.Utils;

namespace MaybeComingBot.Models;

/// <summary>
/// Represents a single scheduled trip for a specific stop and day.
/// </summary>
/// <remarks>
/// Contains departure time, bus route number, and optional note.
/// Provides methods for time conversion, validation, and string representation.
/// </remarks>
public sealed class Trip
{
    private string _time = string.Empty;
    private string _note = string.Empty;
    private int _bus;

    /// <summary>
    /// Initializes a new instance of the <see cref="Trip"/> class.
    /// </summary>
    public Trip() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="Trip"/> class with specified values.
    /// </summary>
    /// <param name="time">The departure time in HH:mm format. Cannot be null.</param>
    /// <param name="bus">The bus route number. Must be greater than 0.</param>
    /// <param name="note">Optional note for the trip. Can be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="time"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="bus"/> is less than or equal to 0.</exception>
    public Trip(string time, int bus, string? note = null)
    {
        Time = time ?? throw new ArgumentNullException(nameof(time));
        Bus = bus > 0 ? bus : throw new ArgumentException("Bus must be greater than 0", nameof(bus));
        Note = note ?? string.Empty;
    }

    /// <summary>
    /// Gets or sets the departure time in HH:mm format.
    /// </summary>
    /// <value>
    /// A string representing the time. Never returns null.
    /// Valid formats are "HH:mm", "H:mm", or "24:00" for end of day.
    /// </value>
    public string Time
    {
        get => _time;
        set => _time = value ?? string.Empty;
    }

    /// <summary>
    /// Gets or sets the bus route number.
    /// </summary>
    /// <value>
    /// A positive integer representing the bus number.
    /// Values less than or equal to 0 are automatically clamped to 0.
    /// </value>
    public int Bus
    {
        get => _bus;
        set => _bus = value > 0 ? value : 0;
    }

    /// <summary>
    /// Gets or sets an optional note for this trip.
    /// </summary>
    /// <value>
    /// A string containing additional information. Never returns null.
    /// Can be empty if no note is provided.
    /// </value>
    public string Note
    {
        get => _note;
        set => _note = value ?? string.Empty;
    }

    /// <summary>
    /// Converts the trip time to minutes since midnight.
    /// </summary>
    /// <returns>
    /// The time in minutes since midnight, or <c>null</c> if the time format is invalid.
    /// </returns>
    /// <remarks>
    /// Uses <see cref="TimeHelper.ToMinutes"/> for the conversion.
    /// The special value "24:00" is converted to <see cref="TimeConstants.MaxMinutesInDay"/> (1440).
    /// </remarks>
    public int? ToMinutes()
    {
        return TimeHelper.ToMinutes(Time);
    }

    /// <summary>
    /// Converts the trip time to minutes since midnight with a default value.
    /// </summary>
    /// <param name="defaultValue">The default value to return if parsing fails.</param>
    /// <returns>
    /// The time in minutes since midnight, or <paramref name="defaultValue"/> if invalid.
    /// </returns>
    public int ToMinutesSafe(int defaultValue = 0)
    {
        return TimeHelper.ToMinutesSafe(Time, defaultValue);
    }

    /// <summary>
    /// Validates the trip data.
    /// </summary>
    /// <returns>
    /// <c>true</c> if the trip has a valid time and bus number; otherwise, <c>false</c>.
    /// </returns>
    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(Time) &&
               TimeHelper.IsValidTime(Time) &&
               Bus > 0;
    }

    /// <summary>
    /// Returns a string representation of the trip.
    /// </summary>
    /// <returns>
    /// A string containing the time, bus number, and optional note.
    /// Example: "14:30 (Bus 42)" or "14:30 (Bus 42, Express)".
    /// </returns>
    public override string ToString()
    {
        return string.IsNullOrEmpty(Note)
            ? $"{Time} (Bus {Bus})"
            : $"{Time} (Bus {Bus}, {Note})";
    }
}