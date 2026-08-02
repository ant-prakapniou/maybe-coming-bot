using MaybeComingBot.Models;
using Microsoft.Extensions.Options;

namespace MaybeComingBot.Utils;

/// <summary>
/// Provides custom validation logic for <see cref="BotSettings"/> beyond DataAnnotations.
/// </summary>
/// <remarks>
/// This validator performs cross-property validation and business rule checks
/// that cannot be expressed with DataAnnotation attributes alone.
/// It is registered as <see cref="IValidateOptions{BotSettings}"/> and runs
/// at application startup via <c>ValidateOnStart()</c>.
/// </remarks>
public class BotSettingsValidator : IValidateOptions<BotSettings>
{
    /// <summary>
    /// Validates the specified <see cref="BotSettings"/> instance.
    /// </summary>
    /// <param name="name">The options name (not used in this implementation).</param>
    /// <param name="options">The settings instance to validate.</param>
    /// <returns>
    /// A <see cref="ValidateOptionsResult"/> indicating success or failure with error messages.
    /// </returns>
    /// <remarks>
    /// Performs the following validation checks:
    /// <list type="number">
    /// <item><description>BotToken is not null or whitespace</description></item>
    /// <item><description>ScheduleUrl is a valid absolute URL with HTTP/HTTPS scheme</description></item>
    /// <item><description>AdminUserIds contains at least one positive ID</description></item>
    /// <item><description>UpdateIntervalHours does not exceed UserStateTtlHours</description></item>
    /// <item><description>Rate limit configuration is not too aggressive</description></item>
    /// <item><description>TimeoutSeconds is sufficient for the configured retry count</description></item>
    /// </list>
    /// DataAnnotation validation is already handled by <c>ValidateDataAnnotations()</c>
    /// in the service registration. This validator focuses on cross-property constraints.
    /// </remarks>
    public ValidateOptionsResult Validate(string? name, BotSettings options)
    {
        // ============================================================
        // TOKEN VALIDATION
        // ============================================================

        if (string.IsNullOrWhiteSpace(options.BotToken))
        {
            return ValidateOptionsResult.Fail("BotToken is required");
        }

        // ============================================================
        // URL VALIDATION
        // ============================================================

        if (!Uri.TryCreate(options.ScheduleUrl, UriKind.Absolute, out var uri))
        {
            return ValidateOptionsResult.Fail("ScheduleUrl must be a valid absolute URL");
        }

        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
        {
            return ValidateOptionsResult.Fail("ScheduleUrl must use HTTP or HTTPS scheme");
        }

        // ============================================================
        // ADMIN USER VALIDATION
        // ============================================================

        if (options.AdminUserIds is null || options.AdminUserIds.Length == 0)
        {
            return ValidateOptionsResult.Fail("At least one admin user ID must be specified");
        }

        foreach (var adminId in options.AdminUserIds)
        {
            if (adminId <= 0)
            {
                return ValidateOptionsResult.Fail($"Invalid admin user ID: {adminId}. Must be positive.");
            }
        }

        // ============================================================
        // SUPPORT USERNAME VALIDATION (optional, privacy-safe contact)
        // ============================================================

        if (!string.IsNullOrWhiteSpace(options.SupportUsername))
        {
            var username = options.SupportUsername;

            // Telegram usernames are 5-32 characters: letters, digits, underscore.
            if (username.Length < 5 || username.Length > 32)
            {
                return ValidateOptionsResult.Fail(
                    $"SupportUsername must be between 5 and 32 characters (got {username.Length}).");
            }

            if (!username.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
            {
                return ValidateOptionsResult.Fail(
                    "SupportUsername may only contain letters, digits, and underscores.");
            }
        }

        // ============================================================
        // CROSS-PROPERTY VALIDATION
        // ============================================================

        if (options.UpdateIntervalHours > 0 && options.UpdateIntervalHours > options.UserStateTtlHours)
        {
            return ValidateOptionsResult.Fail(
                $"UpdateIntervalHours ({options.UpdateIntervalHours}h) cannot exceed UserStateTtlHours ({options.UserStateTtlHours}h). " +
                "User states would expire before schedule updates.");
        }

        // ============================================================
        // RATE LIMIT VALIDATION
        // ============================================================

        if (options.RateLimitMaxRequests > 10 && options.RateLimitWindowMinutes < 2)
        {
            return ValidateOptionsResult.Fail(
                $"Rate limit is too aggressive: MaxRequests={options.RateLimitMaxRequests} with Window={options.RateLimitWindowMinutes}min. " +
                "Consider reducing MaxRequests or increasing Window.");
        }

        // ============================================================
        // TIMEOUT VALIDATION
        // ============================================================

        if (options.TimeoutSeconds < 10 && options.MaxRetryCount > 1)
        {
            return ValidateOptionsResult.Fail(
                $"TimeoutSeconds ({options.TimeoutSeconds}s) is too low for MaxRetryCount={options.MaxRetryCount}. " +
                "Consider increasing TimeoutSeconds to at least 30s to allow for retry attempts.");
        }

        // ============================================================
        // SUCCESS
        // ============================================================

        return ValidateOptionsResult.Success;
    }
}