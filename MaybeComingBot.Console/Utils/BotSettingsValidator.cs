using MaybeComingBot.Models;
using Microsoft.Extensions.Options;

namespace MaybeComingBot.Utils;

public class BotSettingsValidator : IValidateOptions<BotSettings>
{
    public ValidateOptionsResult Validate(string? name, BotSettings options)
    {
        // DataAnnotations уже проверены через ValidateDataAnnotations()
        // Здесь только кастомные проверки

        // Проверка BotToken
        if (string.IsNullOrWhiteSpace(options.BotToken))
        {
            return ValidateOptionsResult.Fail("BotToken is required");
        }

        // Проверка ScheduleUrl
        if (!Uri.TryCreate(options.ScheduleUrl, UriKind.Absolute, out var uri))
        {
            return ValidateOptionsResult.Fail("ScheduleUrl must be a valid absolute URL");
        }

        // Проверка, что URL использует HTTPS или HTTP
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
        {
            return ValidateOptionsResult.Fail("ScheduleUrl must use HTTP or HTTPS scheme");
        }

        // Проверка, что AdminUserIds не пустой
        if (options.AdminUserIds is null || options.AdminUserIds.Length == 0)
        {
            return ValidateOptionsResult.Fail("At least one admin user ID must be specified");
        }

        // Проверка, что все AdminUserIds положительные
        foreach (var adminId in options.AdminUserIds)
        {
            if (adminId <= 0)
            {
                return ValidateOptionsResult.Fail($"Invalid admin user ID: {adminId}. Must be positive.");
            }
        }

        // Проверка, что UpdateIntervalHours не превышает UserStateTtlHours
        if (options.UpdateIntervalHours > 0 && options.UpdateIntervalHours > options.UserStateTtlHours)
        {
            return ValidateOptionsResult.Fail(
                $"UpdateIntervalHours ({options.UpdateIntervalHours}h) cannot exceed UserStateTtlHours ({options.UserStateTtlHours}h)");
        }

        // Проверка RateLimit
        if (options.RateLimitMaxRequests > 10 && options.RateLimitWindowMinutes < 2)
        {
            return ValidateOptionsResult.Fail(
                $"Rate limit is too aggressive: MaxRequests={options.RateLimitMaxRequests} with Window={options.RateLimitWindowMinutes}min. " +
                "Consider reducing MaxRequests or increasing Window.");
        }

        // Проверка TimeoutSeconds
        if (options.TimeoutSeconds < 10 && options.MaxRetryCount > 1)
        {
            return ValidateOptionsResult.Fail(
                $"TimeoutSeconds ({options.TimeoutSeconds}s) is too low for MaxRetryCount={options.MaxRetryCount}. " +
                "Consider increasing TimeoutSeconds to at least 30s.");
        }

        // Все проверки пройдены
        return ValidateOptionsResult.Success;
    }
}