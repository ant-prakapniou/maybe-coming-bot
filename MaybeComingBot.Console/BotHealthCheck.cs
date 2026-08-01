using MaybeComingBot.Abstractions;
using MaybeComingBot.Utils;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using MaybeComingBot.Models;
using System.Collections.Generic;

namespace MaybeComingBot;

public class BotHealthCheck(
    IScheduleRefreshService refreshService,
    IScheduleStorage storage,
    IOptions<BotSettings> settings) : IHealthCheck
{
    private readonly IScheduleRefreshService _refreshService = refreshService;
    private readonly IScheduleStorage _storage = storage;
    private readonly int _updateIntervalHours = settings.Value.UpdateIntervalHours;
    private const int DefaultMaxAgeHours = 48;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // Проверка наличия сервисов
        if (_refreshService is null)
            return Task.FromResult(HealthCheckResult.Unhealthy("Refresh service is not available"));

        if (_storage is null)
            return Task.FromResult(HealthCheckResult.Unhealthy("Storage service is not available"));

        // Проверка наличия данных
        if (!_storage.HasData())
            return Task.FromResult(HealthCheckResult.Unhealthy("Schedule data is not loaded"));

        // Проверка, что есть хотя бы одна остановка
        if (_storage.StopCount == 0)
            return Task.FromResult(HealthCheckResult.Unhealthy("No stops available in schedule"));

        // Проверка наличия рейсов
        if (_storage.CachedTripsCount == 0)
            return Task.FromResult(HealthCheckResult.Degraded("Schedule loaded but no trips available"));

        // Проверка последнего обновления
        var lastUpdate = _refreshService.LastSuccessfulUpdate;
        if (lastUpdate == DateTime.MinValue)
            return Task.FromResult(HealthCheckResult.Unhealthy("Schedule has never been updated"));

        // Проверка возраста данных
        var age = DateTime.UtcNow - lastUpdate;
        var maxAge = _updateIntervalHours > 0
            ? TimeSpan.FromHours(_updateIntervalHours * 2)
            : TimeSpan.FromHours(DefaultMaxAgeHours);

        // Создаем словарь с данными (используем IReadOnlyDictionary)
        var data = new Dictionary<string, object>
        {
            ["StopCount"] = _storage.StopCount,
            ["TripsCount"] = _storage.CachedTripsCount,
            ["LastUpdate"] = lastUpdate,
            ["AgeMinutes"] = age.TotalMinutes,
            ["FailureCount"] = _refreshService.FailureCount
        };

        if (age > maxAge)
        {
            var ageStr = TimeHelper.FormatTimeSpan((int)age.TotalMinutes);
            var maxAgeStr = TimeHelper.FormatTimeSpan((int)maxAge.TotalMinutes);

            return Task.FromResult(HealthCheckResult.Degraded(
                $"Schedule data is stale (last update: {ageStr} ago, max age: {maxAgeStr})",
                null, // Exception
                data)); // IReadOnlyDictionary
        }

        // Проверка количества ошибок обновления
        if (_refreshService.FailureCount > 10)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                $"High failure rate: {_refreshService.FailureCount} failed refresh attempts",
                null, // Exception
                data));
        }

        // Всё хорошо
        var healthyMessage = $"Schedule data is valid ({_storage.StopCount} stops, " +
                            $"last update: {TimeHelper.FormatTimeSpan((int)age.TotalMinutes)} ago)";

        data["Status"] = "Healthy";

        return Task.FromResult(HealthCheckResult.Healthy(healthyMessage, data));
    }
}