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
/// <para>
/// This health check validates the bot's operational status including:
/// <list type="bullet">
/// <item><description>Availability of core services (refresh, storage)</description></item>
/// <item><description>Schedule data presence and completeness</description></item>
/// <item><description>Data freshness based on update interval</description></item>
/// <item><description>Success rate of refresh attempts</description></item>
/// </list>
/// </para>
/// <para>
/// The health check returns:
/// <list type="bullet">
/// <item><description><see cref="HealthStatus.Healthy"/> - All systems operational</description></item>
/// <item><description><see cref="HealthStatus.Degraded"/> - Schedule still loading, stale, or refresh issues</description></item>
/// <item><description><see cref="HealthStatus.Unhealthy"/> - Core services missing (DI misconfiguration)</description></item>
/// </list>
/// </para>
/// <para>
/// <b>Important:</b> "Schedule data is not loaded yet" is reported as
/// <see cref="HealthStatus.Degraded"/>, not <see cref="HealthStatus.Unhealthy"/>.
/// It is a temporary startup state, and the HTTP health endpoint maps
/// <c>Degraded</c> to 200 so container platforms such as Railway do not stop
/// the container while the initial schedule load is still in progress.
/// </para>
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
    /// <item><description>Service availability (refresh and storage) — Unhealthy if missing</description></item>
    /// <item><description>Data presence — Degraded if the schedule has not loaded yet</description></item>
    /// <item><description>Stop count — Degraded if zero</description></item>
    /// <item><description>Trip availability — Degraded if the cache is empty</description></item>
    /// <item><description>Last update timestamp — Degraded if never updated</description></item>
    /// <item><description>Data freshness — Degraded if stale</description></item>
    /// <item><description>Failure rate — Degraded if more than 10 consecutive failures</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// Only missing core services produce <see cref="HealthStatus.Unhealthy"/>
    /// (→ HTTP 503). Everything else that is "not ready yet" produces
    /// <see cref="HealthStatus.Degraded"/> (→ HTTP 200).
    /// </para>
    /// </remarks>
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // Check service availability — these are DI misconfigurations, not
        // runtime failures, and should surface as Unhealthy.
        if (_refreshService is null)
            return Task.FromResult(HealthCheckResult.Unhealthy("Refresh service is not available"));

        if (_storage is null)
            return Task.FromResult(HealthCheckResult.Unhealthy("Storage service is not available"));

        // From here on, "not ready yet" is Degraded, not Unhealthy.

        var data = new Dictionary<string, object>
        {
            ["StopCount"] = _storage.StopCount,
            ["TripsCount"] = _storage.CachedTripsCount,
            ["LastUpdate"] = _storage.LastUpdated,
            ["FailureCount"] = _refreshService.FailureCount
        };

        if (!_storage.HasData())
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                "Schedule data is not loaded yet (startup in progress)", null, data));
        }

        if (_storage.StopCount == 0)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                "Schedule loaded but contains no stops", null, data));
        }

        if (_storage.CachedTripsCount == 0)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                "Schedule loaded but no trips available", null, data));
        }

        var lastUpdate = _storage.LastUpdated;
        if (lastUpdate == DateTime.MinValue)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                "Schedule has never been updated", null, data));
        }

        var age = DateTime.UtcNow - lastUpdate;
        var maxAge = _updateIntervalHours > 0
            ? TimeSpan.FromHours(_updateIntervalHours * 2)
            : TimeSpan.FromHours(DefaultMaxAgeHours);

        data["AgeMinutes"] = age.TotalMinutes;

        if (age > maxAge)
        {
            var ageStr = TimeHelper.FormatTimeSpan((int)age.TotalMinutes);
            var maxAgeStr = TimeHelper.FormatTimeSpan((int)maxAge.TotalMinutes);

            return Task.FromResult(HealthCheckResult.Degraded(
                $"Schedule data is stale (last update: {ageStr} ago, max age: {maxAgeStr})",
                null,
                data));
        }

        if (_refreshService.FailureCount > 10)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                $"High failure rate: {_refreshService.FailureCount} failed refresh attempts",
                null,
                data));
        }

        var healthyMessage = $"Schedule data is valid ({_storage.StopCount} stops, " +
                            $"last update: {TimeHelper.FormatTimeSpan((int)age.TotalMinutes)} ago)";

        data["Status"] = "Healthy";

        return Task.FromResult(HealthCheckResult.Healthy(healthyMessage, data));
    }
}