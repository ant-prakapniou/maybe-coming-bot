using MaybeComingBot.Abstractions;
using MaybeComingBot.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace MaybeComingBot.Services;

public class ScheduleStorage : IScheduleStorage
{
    private ScheduleRoot _data = new() { Stops = [] };
    private volatile Dictionary<string, Stop> _stopDict = new();
    private volatile ConcurrentDictionary<string, IReadOnlyList<Trip>> _tripCache = new();
    private volatile List<Stop> _cachedStops = [];
    private volatile bool _isLoaded;

    private readonly ILogger<ScheduleStorage> _logger;
    private readonly SemaphoreSlim _updateLock = new(1, 1);

    public ScheduleStorage(ILogger<ScheduleStorage> logger)
    {
        _logger = logger;
    }

    public IEnumerable<Stop> GetAllStops() => _isLoaded ? _cachedStops : [];

    public Stop? GetStop(string id)
    {
        if (string.IsNullOrEmpty(id) || !_isLoaded)
            return null;

        _stopDict.TryGetValue(id, out var stop);
        return stop;
    }

    public IReadOnlyList<Trip>? GetTripsForDay(string stopId, int day)
    {
        if (string.IsNullOrWhiteSpace(stopId) || !_isLoaded)
            return null;

        var key = $"{stopId}_{day}";
        _tripCache.TryGetValue(key, out var trips);
        return trips;
    }

    public void Update(ScheduleRoot newData)
    {
        if (newData is null)
        {
            _logger.LogWarning("Update called with null");
            return;
        }

        // Строим новые структуры ДО блокировки
        var (newStopDict, newTripCache, newCachedStops, hasData) = BuildDataStructures(newData);

        // Блокируем ТОЛЬКО на время замены ссылок (микросекунды)
        _updateLock.Wait();
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
            _updateLock.Release();
        }

        _logger.LogDebug("Updated: {Stops} stops, {Trips} trip caches",
            newCachedStops.Count, newTripCache.Count);
    }

    private (Dictionary<string, Stop> StopDict, ConcurrentDictionary<string, IReadOnlyList<Trip>> TripCache, List<Stop> CachedStops, bool HasData) BuildDataStructures(ScheduleRoot newData)
    {
        if (newData.Stops is null || newData.Stops.Count == 0)
        {
            return (new Dictionary<string, Stop>(), new ConcurrentDictionary<string, IReadOnlyList<Trip>>(), [], false);
        }

        var stopDict = new Dictionary<string, Stop>(newData.Stops.Count);
        var tripCache = new ConcurrentDictionary<string, IReadOnlyList<Trip>>();
        var cachedStops = new List<Stop>(newData.Stops.Count);

        foreach (var stop in newData.Stops)
        {
            if (stop is null || string.IsNullOrEmpty(stop.Id))
                continue;

            // Глубокая копия остановки
            var clonedStop = CloneStop(stop);

            if (!stopDict.TryAdd(stop.Id, clonedStop))
            {
                _logger.LogWarning("Duplicate stop ID: {StopId}", stop.Id);
                continue;
            }

            cachedStops.Add(clonedStop);

            // Строим кэш по дням
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
                        tripCache.TryAdd(key, tripsCopy.AsReadOnly());
                    }
                }
            }
        }

        return (stopDict, tripCache, cachedStops, cachedStops.Count > 0);
    }

    private static Stop CloneStop(Stop source)
    {
        var cloned = new Stop
        {
            Id = source.Id,
            Name = source.Name ?? string.Empty,
            Schedule = []
        };

        if (source.Schedule is not null)
        {
            foreach (var daySchedule in source.Schedule)
            {
                if (daySchedule is null)
                    continue;

                var clonedDay = new DaySchedule
                {
                    Day = daySchedule.Day,
                    Trips = []
                };

                if (daySchedule.Trips is not null)
                {
                    foreach (var trip in daySchedule.Trips)
                    {
                        if (trip is null)
                            continue;

                        clonedDay.Trips.Add(new Trip
                        {
                            Time = trip.Time ?? string.Empty,
                            Bus = trip.Bus,
                            Note = trip.Note ?? string.Empty
                        });
                    }
                }

                cloned.Schedule.Add(clonedDay);
            }
        }

        return cloned;
    }

    public bool HasData() => _isLoaded;
    public int StopCount => _cachedStops?.Count ?? 0;
    public int CachedTripsCount => _tripCache?.Count ?? 0;
    public bool IsLoaded => _isLoaded;
}