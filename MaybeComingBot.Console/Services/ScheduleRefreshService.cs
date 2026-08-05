using MaybeComingBot.Abstractions;
using MaybeComingBot.Constants;
using MaybeComingBot.Models;
using MaybeComingBot.Utils;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MaybeComingBot.Services;

/// <summary>
/// Background service that periodically refreshes the schedule data.
/// </summary>
/// <remarks>
/// This service runs as a hosted service and handles:
/// <list type="bullet">
/// <item><description>Initial schedule loading at startup</description></item>
/// <item><description>Periodic refresh at configured intervals</description></item>
/// <item><description>Exponential backoff with jitter on failures</description></item>
/// <item><description>Thread-safe refresh operations using a semaphore</description></item>
/// <item><description>Status reporting for monitoring</description></item>
/// </list>
/// </remarks>
/// <param name="loader">The schedule loader for fetching data.</param>
/// <param name="storage">The storage for persisting schedule data.</param>
/// <param name="settings">The bot settings containing refresh configuration.</param>
/// <param name="timeZone">The configured timezone used for status timestamps.</param>
/// <param name="logger">The logger instance for this class.</param>
public class ScheduleRefreshService(
    ScheduleLoader loader,
    IScheduleStorage storage,
    IOptions<BotSettings> settings,
    TimeZoneInfo timeZone,
    ILogger<ScheduleRefreshService> logger) : BackgroundService, IScheduleRefreshService
{
    private const int MaxBackoffFailureCount = 30;
    private const int InitialRetryDelaySeconds = 30;
    private const int LoopErrorDelayMinutes = 5;

    private readonly ScheduleLoader _loader = loader;
    private readonly IScheduleStorage _storage = storage;
    private readonly TimeZoneInfo _timeZone = timeZone;
    private readonly int? _intervalHours = settings.Value.UpdateIntervalHours > 0
            ? settings.Value.UpdateIntervalHours
            : (int?)null;
    private readonly ILogger<ScheduleRefreshService> _logger = logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly Random _jitter = new();

    private int _failureCount;
    private DateTime _lastSuccessfulUpdate = DateTime.MinValue;
    private DateTime _lastAttemptTime = DateTime.MinValue;

    /// <inheritdoc/>
    public DateTime LastSuccessfulUpdate => _lastSuccessfulUpdate;

    /// <inheritdoc/>
    public int FailureCount => Interlocked.CompareExchange(ref _failureCount, 0, 0);

    /// <inheritdoc/>
    public override void Dispose()
    {
        _refreshLock.Dispose();
        base.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Executes the background service logic.
    /// </summary>
    /// <param name="stoppingToken">Cancellation token for service shutdown.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// The execution flow:
    /// <list type="number">
    /// <item><description>Performs initial schedule load</description></item>
    /// <item><description>Retries initial load once if it fails</description></item>
    /// <item><description>If auto-update is disabled, waits indefinitely</description></item>
    /// <item><description>Main loop: calculates delay, waits, and refreshes</description></item>
    /// </list>
    /// </remarks>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Refresh service started");

        // Initial load
        try
        {
            await RefreshScheduleWithLockAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Initial refresh failed");
            throw;
        }

        // Retry initial load if no data
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

        // If auto-update is disabled, wait indefinitely
        if (!_intervalHours.HasValue)
        {
            _logger.LogInformation("Auto-update disabled, waiting indefinitely");
            try
            {
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
            }
            return;
        }

        // Main update loop
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
                // Continue with delay instead of crashing
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

    /// <inheritdoc/>
    public async Task ForceRefreshAsync(CancellationToken ct = default)
    {
        await RefreshScheduleWithLockAsync(ct);
    }

    /// <inheritdoc/>
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
            return $"⚠️ DATA STALE: {FormatStatusTime(_lastSuccessfulUpdate)} ({ageStr})";

        return $"✅ LAST: {FormatStatusTime(_lastSuccessfulUpdate)} ({ageStr})";
    }

    /// <inheritdoc/>
    public string GetLastAttemptInfo()
    {
        if (_lastAttemptTime == DateTime.MinValue)
            return "NO ATTEMPTS";

        var age = DateTime.UtcNow - _lastAttemptTime;
        var ageMinutes = (int)Math.Min(age.TotalMinutes, int.MaxValue);
        var failureCount = FailureCount;

        return $"ATTEMPT: {FormatStatusTime(_lastAttemptTime)} ({TimeHelper.FormatTimeSpan(ageMinutes)}), FAILS: {failureCount}";
    }

    /// <summary>
    /// Converts a UTC timestamp to the configured timezone for display.
    /// </summary>
    /// <param name="utcTime">The UTC timestamp to convert.</param>
    /// <returns>A formatted HH:mm:ss string in the configured timezone.</returns>
    private string FormatStatusTime(DateTime utcTime)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(utcTime, _timeZone);
        return local.ToString("HH:mm:ss");
    }

    /// <summary>
    /// Performs a refresh operation with locking to prevent concurrent execution.
    /// </summary>
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

    /// <summary>
    /// Internal refresh logic that loads and updates the schedule.
    /// </summary>
    private async Task RefreshScheduleInternalAsync(CancellationToken ct)
    {
        _lastAttemptTime = DateTime.UtcNow;

        try
        {
            var schedule = await _loader.LoadAsync(ct);

            if (schedule?.Stops is null || schedule.Stops.Count == 0)
                throw new InvalidOperationException("Schedule empty");

            _storage.Update(schedule);

            // Atomic reset of failure counter
            Interlocked.Exchange(ref _failureCount, 0);
            _lastSuccessfulUpdate = DateTime.UtcNow;

            _logger.LogInformation("✅ Refresh success: {Count} stops", schedule.Stops.Count);
        }
        catch (Exception ex)
        {
            // Atomic increment and retrieve
            var failureCount = Interlocked.Increment(ref _failureCount);
            _logger.LogError(ex, "❌ Refresh failed (failures: {Count})", failureCount);
            throw;
        }
    }

    /// <summary>
    /// Calculates the delay until the next refresh attempt.
    /// </summary>
    /// <returns>
    /// A time span representing the delay.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The delay calculation:
    /// <list type="bullet">
    /// <item><description>If no failures: uses the configured interval</description></item>
    /// <item><description>If failures exist: uses exponential backoff: BaseRetryDelay × 2^(failures - 1)</description></item>
    /// <item><description>Applies ±10% jitter to prevent thundering herd</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    private TimeSpan CalculateDelay()
    {
        var failureCount = FailureCount;

        if (failureCount <= 0)
            return TimeSpan.FromHours(_intervalHours!.Value);

        // Safe backoff calculation with limits
        var effectiveFailures = Math.Min(failureCount, MaxBackoffFailureCount);
        var backoffMinutes = Math.Min(
            TimeConstants.BaseRetryDelayMinutes * Math.Pow(2, effectiveFailures - 1),
            TimeConstants.MaxRetryDelayMinutes);

        // Add jitter to prevent thundering herd
        var jitterFactor = 0.9 + (0.2 * _jitter.NextDouble());
        var finalDelay = TimeSpan.FromMinutes(backoffMinutes * jitterFactor);

        _logger.LogDebug("Calculated delay: {Delay:F1} minutes (failures: {Count})",
            finalDelay.TotalMinutes, failureCount);

        return finalDelay;
    }
}
