using MaybeComingBot.Models;

namespace MaybeComingBot.Abstractions;

/// <summary>
/// Defines the contract for schedule data storage and retrieval.
/// </summary>
/// <remarks>
/// This interface provides thread-safe access to the schedule data stored in memory.
/// Implementations should ensure atomic updates and consistent reads across multiple threads.
/// </remarks>
public interface IScheduleStorage
{
    /// <summary>
    /// Gets all stops in the schedule.
    /// </summary>
    /// <returns>
    /// A read-only list of <see cref="Stop"/> objects.
    /// Returns an empty list if no data is loaded.
    /// </returns>
    IReadOnlyList<Stop> GetAllStops();

    /// <summary>
    /// Retrieves a specific stop by its unique identifier.
    /// </summary>
    /// <param name="id">The stop identifier. Cannot be null or empty.</param>
    /// <returns>
    /// The <see cref="Stop"/> if found; otherwise, <c>null</c>.
    /// </returns>
    Stop? GetStop(string id);

    /// <summary>
    /// Retrieves trips for a specific stop on a given day.
    /// </summary>
    /// <param name="stopId">The stop identifier. Cannot be null or empty.</param>
    /// <param name="day">The day of the week (0 = Sunday, 6 = Saturday).</param>
    /// <returns>
    /// A read-only list of <see cref="Trip"/> objects, or <c>null</c> if no trips exist.
    /// </returns>
    /// <remarks>
    /// Returns an empty list if the stop or day has no trips.
    /// The returned list is cached for performance and should not be modified.
    /// </remarks>
    IReadOnlyList<Trip>? GetTripsForDay(string stopId, int day);

    /// <summary>
    /// Updates the entire schedule with new data.
    /// </summary>
    /// <param name="newData">The new schedule data. Cannot be null.</param>
    /// <remarks>
    /// This method performs an atomic replacement of all data.
    /// The update is thread-safe and internally uses read-write locks.
    /// </remarks>
    void Update(ScheduleRoot newData);

    /// <summary>
    /// Determines whether the storage contains any schedule data.
    /// </summary>
    /// <returns>
    /// <c>true</c> if schedule data is loaded; otherwise, <c>false</c>.
    /// </returns>
    bool HasData();

    /// <summary>
    /// Gets the total number of stops in the schedule.
    /// </summary>
    /// <value>
    /// The count of stops, or 0 if no data is loaded.
    /// </value>
    int StopCount { get; }

    /// <summary>
    /// Gets the number of cached trip entries by day.
    /// </summary>
    /// <value>
    /// The total number of cached trip collections (StopId + Day combinations).
    /// </value>
    /// <remarks>
    /// Each entry corresponds to a unique combination of stop and day.
    /// Used for monitoring cache health and performance statistics.
    /// </remarks>
    int CachedTripsCount { get; }

    /// <summary>
    /// Gets a value indicating whether the schedule has been loaded at least once.
    /// </summary>
    /// <value>
    /// <c>true</c> if the schedule has been successfully loaded; otherwise, <c>false</c>.
    /// </value>
    /// <remarks>
    /// Unlike <see cref="HasData"/>, this property only indicates if a load attempt was made.
    /// It does not guarantee that data is present or valid.
    /// </remarks>
    bool IsLoaded { get; }
}