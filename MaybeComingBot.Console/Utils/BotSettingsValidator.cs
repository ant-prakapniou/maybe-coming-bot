using MaybeComingBot.Models;
using Microsoft.Extensions.Options;

namespace MaybeComingBot.Utils;

public class BotSettingsValidator : IValidateOptions<BotSettings>
{
    public ValidateOptionsResult Validate(string? name, BotSettings options)
    {
        // DataAnnotations уже проверены через ValidateDataAnnotations()
        // Здесь только кастомные проверки

        // Проверка ScheduleUrl
        if (!Uri.TryCreate(options.ScheduleUrl, UriKind.Absolute, out var uri))
        {
            return ValidateOptionsResult.Fail("ScheduleUrl must be a valid absolute URL");
        }

        // Проверка, что URL использует HTTPS (опционально)
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
        {
            return ValidateOptionsResult.Fail("ScheduleUrl must use HTTP or HTTPS scheme");
        }

        // Проверка, что AdminUserIds не пустой (дублируется с [MinLength], но оставляем для ясности)
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

        // Проверка, что UpdateIntervalHours не превышает UserStateTtlHours (логическая проверка)
        if (options.UpdateIntervalHours > 0 && options.UpdateIntervalHours > options.UserStateTtlHours)
        {
            // Это не критично, но может быть предупреждением
            // Можно вернуть Success, но с предупреждением
        }

        return ValidateOptionsResult.Success;
    }
}