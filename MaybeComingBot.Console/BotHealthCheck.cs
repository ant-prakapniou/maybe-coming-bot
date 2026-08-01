using MaybeComingBot.Abstractions;
using MaybeComingBot.Utils;
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
        // Проверка наличия данных
        if (!_storage.HasData())
            return Task.FromResult(HealthCheckResult.Unhealthy("Schedule data is not loaded"));

        // Проверка, что есть хотя бы одна остановка
        if (_storage.StopCount == 0)
            return Task.FromResult(HealthCheckResult.Unhealthy("No stops available in schedule"));

        // Проверка последнего обновления
        var lastUpdate = _refreshService.LastSuccessfulUpdate;
        if (lastUpdate == DateTime.MinValue)
            return Task.FromResult(HealthCheckResult.Unhealthy("Schedule has never been updated"));

        // Проверка возраста данных
        var age = DateTime.UtcNow - lastUpdate;
        var maxAge = _updateIntervalHours > 0
            ? TimeSpan.FromHours(_updateIntervalHours * 2)
            : TimeSpan.FromHours(48); // Если обновление отключено, даём 48 часов

        if (age > maxAge)
        {
            var ageMinutes = (int)Math.Min(age.TotalMinutes, int.MaxValue);
            var ageStr = TimeHelper.FormatTimeSpan(ageMinutes);

            return Task.FromResult(HealthCheckResult.Degraded(
                $"Schedule data is stale (last update: {ageStr} ago, max age: {TimeHelper.FormatTimeSpan((int)maxAge.TotalMinutes)})"));
        }

        // Всё хорошо
        var healthyMessage = $"Schedule data is valid ({_storage.StopCount} stops, " +
                            $"last update: {TimeHelper.FormatTimeSpan((int)age.TotalMinutes)} ago)";

        return Task.FromResult(HealthCheckResult.Healthy(healthyMessage));
    }
}