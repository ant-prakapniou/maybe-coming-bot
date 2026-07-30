using MaybeComingBot.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MaybeComingBot;

public class BotHealthCheck(ScheduleRefreshService refreshService) : IHealthCheck
{
    private readonly ScheduleRefreshService _refreshService = refreshService;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (_refreshService.HasValidData())
        {
            return Task.FromResult(HealthCheckResult.Healthy("Schedule data is valid"));
        }

        return Task.FromResult(HealthCheckResult.Unhealthy("Schedule data is not loaded"));
    }
}