using MaybeComingBot.Abstractions;
using MaybeComingBot.Constants;
using MaybeComingBot.Models;
using MaybeComingBot.Utils;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MaybeComingBot.Services;

public class ScheduleRefreshService : BackgroundService
{
    private readonly ScheduleLoader _loader;
    private readonly IScheduleStorage _storage;
    private readonly int _intervalHours;
    private readonly ILogger<ScheduleRefreshService> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private int _failureCount;
    private DateTime _lastSuccessfulUpdate = DateTime.MinValue;
    private DateTime _lastAttemptTime = DateTime.MinValue;

    public ScheduleRefreshService(
        ScheduleLoader loader,
        IScheduleStorage storage,
        IOptions<BotSettings> settings,
        ILogger<ScheduleRefreshService> logger)
    {
        _loader = loader;
        _storage = storage;
        _intervalHours = settings.Value.UpdateIntervalHours;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Refresh service started");

        await RefreshScheduleAsync(stoppingToken);

        if (!_storage.HasData() && _intervalHours > 0)
        {
            _logger.LogWarning("Initial load failed, retrying in 30s");
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            await RefreshScheduleAsync(stoppingToken);
        }

        if (!_storage.HasData())
            _logger.LogCritical("NO SCHEDULE DATA");

        if (_intervalHours <= 0)
        {
            _logger.LogInformation("Auto-update disabled");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var delay = CalculateDelay();
                await Task.Delay(delay, stoppingToken);
                if (stoppingToken.IsCancellationRequested) break;
                await RefreshScheduleAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Refresh loop error");
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }
    }

    public async Task ForceRefreshAsync(CancellationToken ct = default)
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

    public string GetStatus()
    {
        if (_lastSuccessfulUpdate == DateTime.MinValue)
            return "NOT LOADED";

        var age = DateTime.UtcNow - _lastSuccessfulUpdate;
        return $"LAST: {_lastSuccessfulUpdate.ToLocalTime():HH:mm:ss} ({TimeHelper.FormatTimeSpan((int)age.TotalMinutes)})";
    }

    public string GetLastAttemptInfo()
    {
        if (_lastAttemptTime == DateTime.MinValue)
            return "NO ATTEMPTS";

        var age = DateTime.UtcNow - _lastAttemptTime;
        return $"ATTEMPT: {_lastAttemptTime.ToLocalTime():HH:mm:ss} ({TimeHelper.FormatTimeSpan((int)age.TotalMinutes)}), FAILS: {_failureCount}";
    }

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
            var schedule = await _loader.LoadAsync(ct);

            if (schedule?.Stops is null || schedule.Stops.Count == 0)
                throw new InvalidOperationException("Schedule empty");

            _storage.Update(schedule);
            Interlocked.Exchange(ref _failureCount, 0);
            _lastSuccessfulUpdate = DateTime.UtcNow;

            _logger.LogInformation("Refresh success: {Count} stops", schedule.Stops.Count);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _failureCount);
            _logger.LogError(ex, "Refresh failed (failures: {Count})", _failureCount);
            throw;
        }
    }

    private TimeSpan CalculateDelay()
    {
        var failureCount = Interlocked.CompareExchange(ref _failureCount, 0, 0);

        if (failureCount <= 0)
            return TimeSpan.FromHours(_intervalHours);

        int backoff = failureCount > 30
            ? TimeConstants.MaxRetryDelayMinutes
            : Math.Min(TimeConstants.BaseRetryDelayMinutes * (1 << (failureCount - 1)), TimeConstants.MaxRetryDelayMinutes);

        return TimeSpan.FromMinutes(backoff);
    }
}