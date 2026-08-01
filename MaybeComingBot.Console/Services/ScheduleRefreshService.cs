using MaybeComingBot.Abstractions;
using MaybeComingBot.Constants;
using MaybeComingBot.Models;
using MaybeComingBot.Utils;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MaybeComingBot.Services;

public class ScheduleRefreshService : BackgroundService, IScheduleRefreshService
{
    private const int MaxBackoffFailureCount = 30;
    private const int InitialRetryDelaySeconds = 30;
    private const int LoopErrorDelayMinutes = 5;

    private readonly ScheduleLoader _loader;
    private readonly IScheduleStorage _storage;
    private readonly int? _intervalHours;
    private readonly ILogger<ScheduleRefreshService> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly Random _jitter = new();

    private int _failureCount;
    private DateTime _lastSuccessfulUpdate = DateTime.MinValue;
    private DateTime _lastAttemptTime = DateTime.MinValue;

    public DateTime LastSuccessfulUpdate => _lastSuccessfulUpdate;
    public int FailureCount => Interlocked.CompareExchange(ref _failureCount, 0, 0);

    public ScheduleRefreshService(
        ScheduleLoader loader,
        IScheduleStorage storage,
        IOptions<BotSettings> settings,
        ILogger<ScheduleRefreshService> logger)
    {
        _loader = loader;
        _storage = storage;
        _intervalHours = settings.Value.UpdateIntervalHours > 0
            ? settings.Value.UpdateIntervalHours
            : (int?)null;
        _logger = logger;
    }

    public override void Dispose()
    {
        _refreshLock.Dispose();
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Refresh service started");

        // Первичная загрузка
        try
        {
            await RefreshScheduleWithLockAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Initial refresh failed");
            throw;
        }

        // Если данные не загружены, пробуем ещё раз с задержкой
        if (!_storage.HasData() && _intervalHours.HasValue)
        {
            _logger.LogWarning("Initial load failed, retrying in {Delay}s", InitialRetryDelaySeconds);

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(InitialRetryDelaySeconds), stoppingToken);
                await RefreshScheduleWithLockAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Initial retry cancelled");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Retry initial refresh failed");
                throw;
            }
        }

        if (!_storage.HasData())
        {
            _logger.LogCritical("NO SCHEDULE DATA");
            throw new InvalidOperationException("Schedule data could not be loaded");
        }

        // Если авто-обновление отключено - ждём бесконечно
        if (!_intervalHours.HasValue)
        {
            _logger.LogInformation("Auto-update disabled, waiting indefinitely");
            try
            {
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Нормальное завершение
            }
            return;
        }

        // Основной цикл обновления
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var delay = CalculateDelay();
                _logger.LogDebug("Next refresh in {Delay:F1} minutes", delay.TotalMinutes);
                await Task.Delay(delay, stoppingToken);

                if (stoppingToken.IsCancellationRequested)
                    break;

                await RefreshScheduleWithLockAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Refresh loop error");
                // Не вылетаем, продолжаем попытки с задержкой
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(LoopErrorDelayMinutes), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        _logger.LogInformation("Refresh service stopped");
    }

    public async Task ForceRefreshAsync(CancellationToken ct = default)
    {
        await RefreshScheduleWithLockAsync(ct);
    }

    public string GetStatus()
    {
        if (_lastSuccessfulUpdate == DateTime.MinValue)
            return "NOT LOADED";

        var age = DateTime.UtcNow - _lastSuccessfulUpdate;
        var ageMinutes = (int)Math.Min(age.TotalMinutes, int.MaxValue);
        var ageStr = TimeHelper.FormatTimeSpan(ageMinutes);

        var staleThreshold = TimeSpan.FromHours(_intervalHours.HasValue
            ? _intervalHours.Value * 1.5
            : 24);

        if (age > staleThreshold)
            return $"⚠️ DATA STALE: {_lastSuccessfulUpdate.ToLocalTime():HH:mm:ss} ({ageStr})";

        return $"✅ LAST: {_lastSuccessfulUpdate.ToLocalTime():HH:mm:ss} ({ageStr})";
    }

    public string GetLastAttemptInfo()
    {
        if (_lastAttemptTime == DateTime.MinValue)
            return "NO ATTEMPTS";

        var age = DateTime.UtcNow - _lastAttemptTime;
        var ageMinutes = (int)Math.Min(age.TotalMinutes, int.MaxValue);
        var failureCount = FailureCount;

        return $"ATTEMPT: {_lastAttemptTime.ToLocalTime():HH:mm:ss} ({TimeHelper.FormatTimeSpan(ageMinutes)}), FAILS: {failureCount}";
    }

    private async Task RefreshScheduleWithLockAsync(CancellationToken ct)
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

            // ✅ Атомарный сброс счетчика ошибок
            Interlocked.Exchange(ref _failureCount, 0);
            _lastSuccessfulUpdate = DateTime.UtcNow;

            _logger.LogInformation("✅ Refresh success: {Count} stops", schedule.Stops.Count);
        }
        catch (Exception ex)
        {
            // ✅ Атомарное инкрементирование и получение значения
            var failureCount = Interlocked.Increment(ref _failureCount);
            _logger.LogError(ex, "❌ Refresh failed (failures: {Count})", failureCount);
            throw;
        }
    }

    private TimeSpan CalculateDelay()
    {
        var failureCount = FailureCount;

        if (failureCount <= 0)
            return TimeSpan.FromHours(_intervalHours!.Value);

        // Безопасный расчет backoff с ограничением
        var effectiveFailures = Math.Min(failureCount, MaxBackoffFailureCount);
        var backoffMinutes = Math.Min(
            TimeConstants.BaseRetryDelayMinutes * Math.Pow(2, effectiveFailures - 1),
            TimeConstants.MaxRetryDelayMinutes);

        // ✅ Добавляем джиттер для предотвращения "thundering herd"
        var jitterFactor = 0.9 + (0.2 * _jitter.NextDouble());
        var finalDelay = TimeSpan.FromMinutes(backoffMinutes * jitterFactor);

        _logger.LogDebug("Calculated delay: {Delay:F1} minutes (failures: {Count})",
            finalDelay.TotalMinutes, failureCount);

        return finalDelay;
    }
}