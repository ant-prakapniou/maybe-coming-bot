using MaybeComingBot.Abstractions;
using MaybeComingBot.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MaybeComingBot;

public class BotHealthCheck(ScheduleRefreshService refreshService, IScheduleStorage storage) : IHealthCheck
{
    private readonly ScheduleRefreshService _refreshService = refreshService;
    private readonly IScheduleStorage _storage = storage;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        return _storage.HasData()
            ? Task.FromResult(HealthCheckResult.Healthy("Schedule data is valid"))
            : Task.FromResult(HealthCheckResult.Unhealthy("Schedule data is not loaded"));
    }
}