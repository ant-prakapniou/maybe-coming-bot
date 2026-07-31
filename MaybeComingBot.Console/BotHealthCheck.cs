using MaybeComingBot.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using MaybeComingBot.Models;

namespace MaybeComingBot;

public class BotHealthCheck(
    IScheduleRefreshService refreshService,
    IScheduleStorage storage,
    IOptions<BotSettings> settings) : IHealthCheck
{
    private readonly IScheduleRefreshService _refreshService = refreshService;
    private readonly IScheduleStorage _storage = storage;
    private readonly int _updateIntervalHours = settings.Value.UpdateIntervalHours;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!_storage.HasData())
            return Task.FromResult(HealthCheckResult.Unhealthy("Schedule data is not loaded"));

        var lastUpdate = _refreshService.LastSuccessfulUpdate;
        if (lastUpdate == DateTime.MinValue)
            return Task.FromResult(HealthCheckResult.Unhealthy("Schedule has never been updated"));

        var age = DateTime.UtcNow - lastUpdate;
        var maxAge = TimeSpan.FromHours(_updateIntervalHours * 2);

        if (age > maxAge)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                $"Schedule data is stale (last update: {age:g} ago)"));
        }

        return Task.FromResult(HealthCheckResult.Healthy($"Schedule data is valid ({_storage.StopCount} stops)"));
    }
}