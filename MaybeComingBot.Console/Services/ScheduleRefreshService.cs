using MaybeComingBot.Constants;
using MaybeComingBot.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Threading;

namespace MaybeComingBot.Services;

public class ScheduleRefreshService : BackgroundService, IAsyncDisposable
{
    private readonly ScheduleLoader _loader;
    private readonly ScheduleStorage _storage;
    private readonly int _intervalHours;
    private readonly ILogger<ScheduleRefreshService> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private int _failureCount;
    private DateTime _lastSuccessfulUpdate = DateTime.MinValue;
    private DateTime _lastAttemptTime = DateTime.MinValue;
    private volatile bool _hasValidData;

    public ScheduleRefreshService(
        ScheduleLoader loader,
        ScheduleStorage storage,
        IOptions<BotSettings> settings,
        ILogger<ScheduleRefreshService> logger)
    {
        _loader = loader;
        _storage = storage;
        _intervalHours = settings.Value.UpdateIntervalHours;
        _logger = logger;
    }

    public async ValueTask DisposeAsync()
    {
        _refreshLock.Dispose();
        await Task.CompletedTask;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Schedule refresh service started.");

        // Первичная загрузка
        await RefreshScheduleAsync(stoppingToken);

        if (!_hasValidData && _intervalHours > 0)
        {
            _logger.LogWarning("No valid data after initial load. Retrying in 30 seconds...");
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            await RefreshScheduleAsync(stoppingToken);
        }

        if (!_hasValidData)
        {
            _logger.LogCritical("CRITICAL: Failed to load schedule on startup. Bot may not work correctly.");
        }

        if (_intervalHours <= 0)
        {
            _logger.LogInformation("Automatic update is disabled (UpdateIntervalHours <= 0).");
            return;
        }

        _logger.LogInformation("Schedule refresh service running. Interval: {IntervalHours} hours.",
            _intervalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var delay = CalculateDelay();

                if (_failureCount > 0)
                {
                    _logger.LogWarning("Next retry in {Delay:g} due to {FailureCount} previous failures",
                        delay, _failureCount);
                }
                else
                {
                    _logger.LogInformation("Next scheduled refresh in {Delay:g}", delay);
                }

                await Task.Delay(delay, stoppingToken);

                if (stoppingToken.IsCancellationRequested) break;

                await RefreshScheduleAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Schedule refresh service stopped.");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in refresh loop");
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }
    }

    public async Task ForceRefreshAsync(CancellationToken ct = default)
    {
        await _refreshLock.WaitAsync(ct);
        try
        {
            _logger.LogInformation("Manual refresh triggered by user");
            await RefreshScheduleInternalAsync(ct);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public string GetStatus()
    {
        if (_lastSuccessfulUpdate == DateTime.MinValue)
            return "Расписание еще не загружено";

        var age = DateTime.UtcNow - _lastSuccessfulUpdate;
        var localTime = _lastSuccessfulUpdate.ToLocalTime();
        return $"Последнее успешное обновление: {localTime:HH:mm:ss} ({(int)age.TotalMinutes} мин. назад)";
    }

    public string GetLastAttemptInfo()
    {
        if (_lastAttemptTime == DateTime.MinValue)
            return "Попыток обновления не было";

        var age = DateTime.UtcNow - _lastAttemptTime;
        var localTime = _lastAttemptTime.ToLocalTime();
        return $"Последняя попытка: {localTime:HH:mm:ss} ({(int)age.TotalMinutes} мин. назад), ошибок: {_failureCount}";
    }

    public bool HasValidData() => _hasValidData;

    private async Task RefreshScheduleAsync(CancellationToken ct)
    {
        await _refreshLock.WaitAsync(ct);
        try
        {
            await RefreshScheduleInternalAsync(ct);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task RefreshScheduleInternalAsync(CancellationToken ct)
    {
        _lastAttemptTime = DateTime.UtcNow;

        try
        {
            _logger.LogInformation("Loading schedule from {Url}", _loader.JsonUrl);

            var schedule = await _loader.LoadAsync(ct);

            if (schedule?.Stops is null || schedule.Stops.Count == 0)
            {
                throw new InvalidOperationException("Loaded schedule is null or has no stops");
            }

            _storage.Update(schedule);

            // Атомарное обновление счетчиков
            Interlocked.Exchange(ref _failureCount, 0);
            _lastSuccessfulUpdate = DateTime.UtcNow;
            _hasValidData = true;

            _logger.LogInformation("Schedule updated successfully. Stops: {StopCount}, Cached trips: {CachedCount}",
                schedule.Stops.Count, _storage.CachedTripsCount);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _failureCount);
            var currentFailures = Interlocked.CompareExchange(ref _failureCount, 0, 0);
            _logger.LogError(ex, "Failed to update schedule (failure count: {FailureCount})", currentFailures);
            throw;
        }
    }

    private TimeSpan CalculateDelay()
    {
        var failureCount = Interlocked.CompareExchange(ref _failureCount, 0, 0);

        if (failureCount <= 0)
            return TimeSpan.FromHours(_intervalHours);

        var minutes = Math.Min(
            TimeConstants.BaseRetryDelayMinutes * (1 << (failureCount - 1)),
            TimeConstants.MaxRetryDelayMinutes);

        return TimeSpan.FromMinutes(minutes);
    }
}