using MaybeComingBot.Abstractions;
using MaybeComingBot.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;

namespace MaybeComingBot.Services;

/// <summary>
/// Thread-safe in-memory storage for schedule data.
/// </summary>
/// <remarks>
/// This class manages the schedule data with read-write locks for thread safety.
/// It provides:
/// <list type="bullet">
/// <item><description>Fast lookups by stop ID using a dictionary</description></item>
/// <item><description>Cached trip lists per day for performance</description></item>
/// <item><description>Atomic updates with deep cloning to prevent mutation</description></item>
/// <item><description>Read-write lock for concurrent access</description></item>
/// </list>
/// </remarks>
public class ScheduleStorage : IScheduleStorage, IDisposable
{
    private ScheduleRoot _data = new() { Stops = [] };
    private Dictionary<string, Stop> _stopDict = new();
    private Dictionary<string, IReadOnlyList<Trip>> _tripCache = new();
    private IReadOnlyList<Stop> _cachedStops = Array.Empty<Stop>();
    private bool _isLoaded;

    private readonly ILogger<ScheduleStorage> _logger;
    private readonly ReaderWriterLockSlim _rwLock = new();

    // Empty collection constants for consistent returns
    private static readonly IReadOnlyList<Stop> EmptyStops = Array.Empty<Stop>();
    private static readonly IReadOnlyList<Trip> EmptyTrips = Array.Empty<Trip>();

    /// <summary>
    /// Initializes a new instance of the <see cref="ScheduleStorage"/> class.
    /// </summary>
    /// <param name="logger">The logger instance for this class.</param>
    public ScheduleStorage(ILogger<ScheduleStorage> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _rwLock.Dispose();
        _logger.LogDebug("ScheduleStorage disposed");
    }

    /// <inheritdoc/>
    public IReadOnlyList<Stop> GetAllStops()
    {
        _rwLock.EnterReadLock();
        try
        {
            return _isLoaded ? _cachedStops : EmptyStops;
        }
        finally
        {
            _rwLock.ExitReadLock();
        }
    }

    /// <inheritdoc/>
    public Stop? GetStop(string id)
    {
        if (string.IsNullOrEmpty(id))
            return null;

        _rwLock.EnterReadLock();
        try
        {
            if (!_isLoaded)
                return null;

            _stopDict.TryGetValue(id, out var stop);
            return stop;
        }
        finally
        {
            _rwLock.ExitReadLock();
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<Trip>? GetTripsForDay(string stopId, int day)
    {
        if (string.IsNullOrWhiteSpace(stopId))
            return null;

        _rwLock.EnterReadLock();
        try
        {
            if (!_isLoaded)
                return null;

            var key = $"{stopId}_{day}";
            return _tripCache.TryGetValue(key, out var trips) ? trips : EmptyTrips;
        }
        finally
        {
            _rwLock.ExitReadLock();
        }
    }

    /// <inheritdoc/>
    public void Update(ScheduleRoot newData)
    {
        if (newData is null)
        {
            _logger.LogWarning("Update called with null");
            return;
        }

        var (newStopDict, newTripCache, newCachedStops, hasData) = BuildDataStructures(newData);

        _rwLock.EnterWriteLock();
        try
        {
            _stopDict = newStopDict;
            _tripCache = newTripCache;
            _cachedStops = newCachedStops;
            _data = newData;
            _isLoaded = hasData;
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }

        _logger.LogDebug("Updated: {Stops} stops, {Trips} trip caches",
            newCachedStops.Count, newTripCache.Count);
    }

    /// <summary>
    /// Builds the internal data structures from the raw schedule data.
    /// </summary>
    /// <param name="newData">The schedule data to process.</param>
    /// <returns>A tuple containing all built data structures.</returns>
    private (Dictionary<string, Stop> StopDict, Dictionary<string, IReadOnlyList<Trip>> TripCache, IReadOnlyList<Stop> CachedStops, bool HasData) BuildDataStructures(ScheduleRoot newData)
    {
        if (newData.Stops is null || newData.Stops.Count == 0)
        {
            return (new Dictionary<string, Stop>(), new Dictionary<string, IReadOnlyList<Trip>>(), EmptyStops, false);
        }

        var stopDict = new Dictionary<string, Stop>(newData.Stops.Count);
        var tripCache = new Dictionary<string, IReadOnlyList<Trip>>(newData.Stops.Count * 7); // Approximately 7 days
        var cachedStops = new List<Stop>(newData.Stops.Count);

        foreach (var stop in newData.Stops)
        {
            if (stop is null || string.IsNullOrEmpty(stop.Id))
                continue;

            var clonedStop = CloneStop(stop);

            if (!stopDict.TryAdd(stop.Id, clonedStop))
            {
                _logger.LogWarning("Duplicate stop ID: {StopId}", stop.Id);
                continue;
            }

            cachedStops.Add(clonedStop);

            // Build day cache
            if (stop.Schedule is not null)
            {
                foreach (var daySchedule in stop.Schedule)
                {
                    if (daySchedule?.Trips is null || daySchedule.Trips.Count == 0)
                        continue;

                    var key = $"{stop.Id}_{daySchedule.Day}";
                    var tripsCopy = daySchedule.Trips
                        .Where(t => t is not null)
                        .Select(t => new Trip
                        {
                            Time = t.Time ?? string.Empty,
                            Bus = t.Bus,
                            Note = t.Note ?? string.Empty
                        })
                        .ToList();

                    if (tripsCopy.Count > 0)
                    {
                        tripCache[key] = tripsCopy.AsReadOnly();
                    }
                }
            }
        }

        return (stopDict, tripCache, cachedStops.AsReadOnly(), cachedStops.Count > 0);
    }

    /// <summary>
    /// Creates a deep clone of a Stop object.
    /// </summary>
    /// <param name="source">The source stop to clone.</param>
    /// <returns>A deep cloned Stop instance.</returns>
    private static Stop CloneStop(Stop source)
    {
        var schedule = new List<DaySchedule>(source.Schedule?.Count ?? 0);

        if (source.Schedule is not null)
        {
            foreach (var daySchedule in source.Schedule)
            {
                if (daySchedule is null)
                    continue;

                var trips = new List<Trip>(daySchedule.Trips?.Count ?? 0);

                if (daySchedule.Trips is not null)
                {
                    foreach (var trip in daySchedule.Trips)
                    {
                        if (trip is null)
                            continue;

                        trips.Add(new Trip
                        {
                            Time = trip.Time ?? string.Empty,
                            Bus = trip.Bus,
                            Note = trip.Note ?? string.Empty
                        });
                    }
                }

                schedule.Add(new DaySchedule
                {
                    Day = daySchedule.Day,
                    Trips = trips
                });
            }
        }

        return new Stop
        {
            Id = source.Id,
            Name = source.Name ?? string.Empty,
            Schedule = schedule
        };
    }

    /// <inheritdoc/>
    public bool HasData()
    {
        _rwLock.EnterReadLock();
        try
        {
            return _isLoaded;
        }
        finally
        {
            _rwLock.ExitReadLock();
        }
    }

    /// <inheritdoc/>
    public int StopCount
    {
        get
        {
            _rwLock.EnterReadLock();
            try
            {
                return _cachedStops?.Count ?? 0;
            }
            finally
            {
                _rwLock.ExitReadLock();
            }
        }
    }

    /// <inheritdoc/>
    public int CachedTripsCount
    {
        get
        {
            _rwLock.EnterReadLock();
            try
            {
                return _tripCache?.Count ?? 0;
            }
            finally
            {
                _rwLock.ExitReadLock();
            }
        }
    }

    /// <inheritdoc/>
    public bool IsLoaded
    {
        get
        {
            _rwLock.EnterReadLock();
            try
            {
                return _isLoaded;
            }
            finally
            {
                _rwLock.ExitReadLock();
            }
        }
    }

    /// <summary>
    /// Clears all schedule data from storage.
    /// </summary>
    public void Clear()
    {
        _rwLock.EnterWriteLock();
        try
        {
            _data = new ScheduleRoot { Stops = [] };
            _stopDict = new Dictionary<string, Stop>();
            _tripCache = new Dictionary<string, IReadOnlyList<Trip>>();
            _cachedStops = EmptyStops;
            _isLoaded = false;
            _logger.LogDebug("ScheduleStorage cleared");
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }
    }
}