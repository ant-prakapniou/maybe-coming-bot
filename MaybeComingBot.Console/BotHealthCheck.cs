using MaybeComingBot.Abstractions;
using MaybeComingBot.Utils;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using MaybeComingBot.Models;
using System.Collections.Generic;

namespace MaybeComingBot;

/// <summary>
/// Provides health check functionality for monitoring bot status.
/// </summary>
/// <remarks>
/// This health check validates the bot's operational status including:
/// <list type="bullet">
/// <item><description>Availability of core services (refresh, storage)</description></item>
/// <item><description>Schedule data presence and completeness</description></item>
/// <item><description>Data freshness based on update interval</description></item>
/// <item><description>Success rate of refresh attempts</description></item>
/// </list>
/// The health check returns:
/// <list type="bullet">
/// <item><description><see cref="HealthStatus.Healthy"/> - All systems operational</description></item>
/// <item><description><see cref="HealthStatus.Degraded"/> - Data stale or refresh issues</description></item>
/// <item><description><see cref="HealthStatus.Unhealthy"/> - Critical failure (no data, missing services)</description></item>
/// </list>
/// </remarks>
public class BotHealthCheck(
    IScheduleRefreshService refreshService,
    IScheduleStorage storage,
    IOptions<BotSettings> settings) : IHealthCheck
{
    private readonly IScheduleRefreshService _refreshService = refreshService;
    private readonly IScheduleStorage _storage = storage;
    private readonly int _updateIntervalHours = settings.Value.UpdateIntervalHours;
    private const int DefaultMaxAgeHours = 48;

    /// <summary>
    /// Performs the health check operation.
    /// </summary>
    /// <param name="context">The health check context.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>
    /// A <see cref="HealthCheckResult"/> containing the health status and diagnostic data.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The health check performs the following validations in order:
    /// <list type="number">
    /// <item><description>Service availability (refresh and storage)</description></item>
    /// <item><description>Data presence (<see cref="IScheduleStorage.HasData"/>)</description></item>
    /// <item><description>Stop count (must have at least one stop)</description></item>
    /// <item><description>Trip availability (at least one trip cached)</description></item>
    /// <item><description>Last update timestamp validity</description></item>
    /// <item><description>Data freshness (age vs max age)</description></item>
    /// <item><description>Failure rate (less than 10 consecutive failures)</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// Diagnostic data is included in the health check result for monitoring purposes.
    /// </para>
    /// </remarks>
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // Check service availability
        if (_refreshService is null)
            return Task.FromResult(HealthCheckResult.Unhealthy("Refresh service is not available"));

        if (_storage is null)
            return Task.FromResult(HealthCheckResult.Unhealthy("Storage service is not available"));

        // Check data presence
        if (!_storage.HasData())
            return Task.FromResult(HealthCheckResult.Unhealthy("Schedule data is not loaded"));

        // Check for at least one stop
        if (_storage.StopCount == 0)
            return Task.FromResult(HealthCheckResult.Unhealthy("No stops available in schedule"));

        // Check for trip availability
        if (_storage.CachedTripsCount == 0)
            return Task.FromResult(HealthCheckResult.Degraded("Schedule loaded but no trips available"));

        // Check last update timestamp (single source of truth: the storage
        // stamps DateTime.UtcNow inside Update when valid data is stored).
        var lastUpdate = _storage.LastUpdated;
        if (lastUpdate == DateTime.MinValue)
            return Task.FromResult(HealthCheckResult.Unhealthy("Schedule has never been updated"));

        // Check data age
        var age = DateTime.UtcNow - lastUpdate;
        var maxAge = _updateIntervalHours > 0
            ? TimeSpan.FromHours(_updateIntervalHours * 2)
            : TimeSpan.FromHours(DefaultMaxAgeHours);

        // Prepare diagnostic data
        var data = new Dictionary<string, object>
        {
            ["StopCount"] = _storage.StopCount,
            ["TripsCount"] = _storage.CachedTripsCount,
            ["LastUpdate"] = lastUpdate,
            ["AgeMinutes"] = age.TotalMinutes,
            ["FailureCount"] = _refreshService.FailureCount
        };

        // Check for stale data
        if (age > maxAge)
        {
            var ageStr = TimeHelper.FormatTimeSpan((int)age.TotalMinutes);
            var maxAgeStr = TimeHelper.FormatTimeSpan((int)maxAge.TotalMinutes);

            return Task.FromResult(HealthCheckResult.Degraded(
                $"Schedule data is stale (last update: {ageStr} ago, max age: {maxAgeStr})",
                null, // Exception
                data));
        }

        // Check failure rate
        if (_refreshService.FailureCount > 10)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                $"High failure rate: {_refreshService.FailureCount} failed refresh attempts",
                null, // Exception
                data));
        }

        // All checks passed
        var healthyMessage = $"Schedule data is valid ({_storage.StopCount} stops, " +
                            $"last update: {TimeHelper.FormatTimeSpan((int)age.TotalMinutes)} ago)";

        data["Status"] = "Healthy";

        return Task.FromResult(HealthCheckResult.Healthy(healthyMessage, data));
    }
}