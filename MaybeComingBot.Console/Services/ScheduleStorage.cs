using MaybeComingBot.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace MaybeComingBot.Services;

public class ScheduleStorage
{
    private ScheduleRoot _data = new() { Stops = [] };
    private Dictionary<string, Stop> _stopDict = [];
    private readonly ConcurrentDictionary<string, List<Trip>> _tripCache = new();
    private readonly ILogger<ScheduleStorage> _logger;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private bool _isLoaded;

    private List<Stop> _cachedStops = [];
    private DateTime _cacheTime = DateTime.MinValue;
    private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(5);

    public ScheduleStorage(ILogger<ScheduleStorage> logger)
    {
        _logger = logger;
    }

    public async Task LoadAsync(Func<Task<ScheduleRoot>> loader, CancellationToken ct = default)
    {
        await _loadLock.WaitAsync(ct);
        try
        {
            if (_isLoaded)
            {
                _logger.LogInformation("Schedule already loaded");
                return;
            }

            _logger.LogInformation("🔄 Loading schedule synchronously...");

            var schedule = await loader();

            if (schedule?.Stops is null || schedule.Stops.Count == 0)
            {
                throw new InvalidOperationException("Schedule is empty or invalid");
            }

            Update(schedule);
            _isLoaded = true;

            _logger.LogInformation("✅ Schedule loaded: {Count} stops", schedule.Stops.Count);
        }
        finally
        {
            _loadLock.Release();
        }
    }

    public IEnumerable<Stop> GetAllStops()
    {
        _logger.LogInformation("🔍 GetAllStops called. IsLoaded={IsLoaded}, DataStopsCount={DataCount}, CachedStopsCount={CachedCount}, CacheAge={CacheAge}s",
            _isLoaded,
            _data.Stops?.Count ?? 0,
            _cachedStops.Count,
            (DateTime.UtcNow - _cacheTime).TotalSeconds);

        if (!_isLoaded)
        {
            _logger.LogWarning("⚠️ GetAllStops called before schedule loaded");
            return [];
        }

        if (_cachedStops.Count > 0 && DateTime.UtcNow - _cacheTime < _cacheDuration)
        {
            _logger.LogInformation("✅ Returning cached stops: {Count}", _cachedStops.Count);
            return _cachedStops;
        }

        _logger.LogInformation("🔄 Cache miss or expired, updating from _data");
        _cachedStops = [.. _data.Stops];
        _cacheTime = DateTime.UtcNow;

        _logger.LogInformation("✅ Returning fresh stops: {Count}", _cachedStops.Count);
        return _cachedStops;
    }

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
        _logger.LogInformation("🔄 Update called with newData={IsNull}, StopsCount={Count}",
            newData is null, newData?.Stops?.Count ?? 0);

        if (newData is null)
        {
            _logger.LogWarning("Update called with null ScheduleRoot");
            return;
        }

        _data = newData;

        if (_data.Stops is null)
        {
            _logger.LogWarning("ScheduleRoot.Stops is null, initializing empty list");
            _data.Stops = [];
        }

        var uniqueStops = new Dictionary<string, Stop>();
        foreach (var stop in _data.Stops)
        {
            if (stop is null || string.IsNullOrEmpty(stop.Id))
            {
                _logger.LogWarning("Found null or empty stop ID, skipping");
                continue;
            }

            if (stop.Schedule is null)
            {
                _logger.LogDebug("Stop {StopId} has null Schedule, initializing empty", stop.Id);
                stop.Schedule = [];
            }

            foreach (var daySchedule in stop.Schedule)
            {
                if (daySchedule.Trips is null)
                {
                    daySchedule.Trips = [];
                }
            }

            if (!uniqueStops.TryAdd(stop.Id, stop))
            {
                _logger.LogWarning(
                    "Duplicate stop ID detected: {StopId}. Keeping first occurrence (Name: {Name})",
                    stop.Id, stop.Name);
            }
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

        // Принудительно обновляем кэш
        _cachedStops = [.. _data.Stops];
        _cacheTime = DateTime.UtcNow;
        _isLoaded = true;

        _logger.LogInformation(
            "✅ Schedule updated. Stops: {StopCount}, Unique: {UniqueCount}, Cached: {CachedCount}, TripCache: {TripCacheCount}",
            _data.Stops.Count, _stopDict.Count, _cachedStops.Count, _tripCache.Count);
    }

    public bool HasData()
    {
        var hasData = _isLoaded && _data.Stops is not null && _data.Stops.Count > 0;
        _logger.LogInformation("🔍 HasData called: {HasData} (IsLoaded={IsLoaded}, StopsCount={Count})",
            hasData, _isLoaded, _data.Stops?.Count ?? 0);
        return hasData;
    }

    public int StopCount => _data.Stops?.Count ?? 0;
    public int CachedTripsCount => _tripCache.Count;
    public bool IsLoaded => _isLoaded;
}