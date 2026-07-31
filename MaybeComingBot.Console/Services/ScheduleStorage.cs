using MaybeComingBot.Abstractions;
using MaybeComingBot.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace MaybeComingBot.Services;

public class ScheduleStorage : IScheduleStorage
{
    private ScheduleRoot _data = new() { Stops = [] };
    private Dictionary<string, Stop> _stopDict = [];
    private readonly ConcurrentDictionary<string, List<Trip>> _tripCache = new();
    private readonly ILogger<ScheduleStorage> _logger;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private volatile bool _isLoaded;
    private volatile List<Stop> _cachedStops = [];

    public ScheduleStorage(ILogger<ScheduleStorage> logger)
    {
        _logger = logger;
    }

    public IEnumerable<Stop> GetAllStops() => _isLoaded ? _cachedStops : [];

    public Stop? GetStop(string id)
    {
        if (string.IsNullOrEmpty(id) || !_isLoaded)
            return null;

        return _stopDict.TryGetValue(id, out var stop) ? stop : null;
    }

    public List<Trip>? GetTripsForDay(string stopId, int day)
    {
        if (string.IsNullOrWhiteSpace(stopId) || !_isLoaded)
            return null;

        var key = $"{stopId}_{day}";
        return _tripCache.TryGetValue(key, out var trips) ? trips : null;
    }

    public void Update(ScheduleRoot newData)
    {
        if (newData is null)
        {
            _logger.LogWarning("Update called with null");
            return;
        }

        lock (_loadLock)
        {
            _data = newData;
            _data.Stops ??= [];

            var uniqueStops = new Dictionary<string, Stop>();
            foreach (var stop in _data.Stops)
            {
                if (stop is null || string.IsNullOrEmpty(stop.Id))
                    continue;

                stop.Schedule ??= [];
                foreach (var daySchedule in stop.Schedule)
                    daySchedule.Trips ??= [];

                uniqueStops.TryAdd(stop.Id, stop);
            }

            _stopDict = uniqueStops;

            _tripCache.Clear();
            foreach (var stop in _data.Stops)
            {
                if (stop.Schedule is null)
                    continue;

                foreach (var daySchedule in stop.Schedule)
                {
                    if (daySchedule.Trips is null || daySchedule.Trips.Count == 0)
                        continue;

                    var key = $"{stop.Id}_{daySchedule.Day}";
                    _tripCache[key] = [.. daySchedule.Trips];
                }
            }

            _cachedStops = [.. _data.Stops];
            _isLoaded = true;
        }

        _logger.LogInformation("Updated: {Stops} stops, {Trips} trip caches",
            _data.Stops.Count, _tripCache.Count);
    }

    public bool HasData() => _isLoaded && _data.Stops is not null && _data.Stops.Count > 0;
    public int StopCount => _data.Stops?.Count ?? 0;
    public int CachedTripsCount => _tripCache.Count;
    public bool IsLoaded => _isLoaded;
}