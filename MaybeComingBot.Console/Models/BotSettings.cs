using System.ComponentModel.DataAnnotations;

namespace MaybeComingBot.Models;

/// <summary>
/// Represents the configuration settings for the Telegram bot.
/// </summary>
/// <remarks>
/// This class is bound to the "BotSettings" section of the application configuration.
/// All properties are validated using DataAnnotations and custom validation logic.
/// </remarks>
public class BotSettings
{
    private string? _botToken;

    /// <summary>
    /// Gets or sets the Telegram Bot API token.
    /// </summary>
    /// <value>
    /// A string containing the bot token obtained from @BotFather.
    /// Must be at least 10 characters long.
    /// </value>
    /// <remarks>
    /// This token is used to authenticate all API requests to Telegram.
    /// Keep this value secure and never commit it to version control.
    /// </remarks>
    [Required(ErrorMessage = "BotToken is required")]
    [MinLength(10, ErrorMessage = "BotToken must be at least 10 characters")]
    public string BotToken
    {
        get => _botToken ?? string.Empty;
        set => _botToken = value ?? string.Empty;
    }

    /// <summary>
    /// Gets or sets the URL of the schedule JSON file.
    /// </summary>
    /// <value>
    /// A valid absolute URL pointing to the schedule data in JSON format.
    /// </value>
    /// <remarks>
    /// The schedule is loaded from this URL at startup and periodically refreshed.
    /// Both HTTP and HTTPS schemes are supported.
    /// </remarks>
    [Required(ErrorMessage = "ScheduleUrl is required")]
    [Url(ErrorMessage = "ScheduleUrl must be a valid URL")]
    public string ScheduleUrl { get; set; } = string.Empty;

    private long[]? _adminUserIds;

    /// <summary>
    /// Gets or sets the list of Telegram user IDs with administrative privileges.
    /// </summary>
    /// <value>
    /// An array of positive long integers representing Telegram user IDs.
    /// Must contain at least one user ID.
    /// </value>
    /// <remarks>
    /// Admins have access to privileged commands such as /update, /status, and receive user reports.
    /// </remarks>
    [Required(ErrorMessage = "At least one AdminUserId is required")]
    [MinLength(1, ErrorMessage = "At least one admin user ID must be specified")]
    public long[] AdminUserIds
    {
        get => _adminUserIds ?? [];
        set => _adminUserIds = value ?? [];
    }

    private string? _supportUsername;

    /// <summary>
    /// Gets or sets the public Telegram username (without the leading "@") used
    /// to let users contact the administrator.
    /// </summary>
    /// <value>
    /// A string containing the admin's Telegram username (e.g., "support_admin").
    /// Optional — when omitted, the bot does not render a contact link.
    /// </value>
    /// <remarks>
    /// This is the privacy-safe alternative to exposing a numeric Telegram user ID.
    /// The value is normalized: leading "@" and surrounding whitespace are stripped.
    /// </remarks>
    public string SupportUsername
    {
        get => NormalizeUsername(_supportUsername);
        set => _supportUsername = value ?? string.Empty;
    }

    /// <summary>
    /// Normalizes a Telegram username by trimming whitespace and stripping a leading "@".
    /// </summary>
    /// <param name="username">The raw username value.</param>
    /// <returns>A normalized username, or an empty string if <paramref name="username"/> is null/empty.</returns>
    private static string NormalizeUsername(string? username)
    {
        if (string.IsNullOrWhiteSpace(username))
            return string.Empty;

        var trimmed = username.Trim();
        return trimmed.StartsWith('@') ? trimmed[1..] : trimmed;
    }

    /// <summary>
    /// Gets or sets the interval between automatic schedule updates.
    /// </summary>
    /// <value>
    /// The update interval in hours. Must be between 0 and 168 (7 days).
    /// Default value is 24 hours.
    /// </value>
    /// <remarks>
    /// A value of 0 disables automatic updates entirely.
    /// When auto-update is disabled, the schedule is only loaded once at startup.
    /// </remarks>
    [Range(0, 168, ErrorMessage = "UpdateIntervalHours must be between 0 and 168 (7 days)")]
    public int UpdateIntervalHours { get; set; } = 24;

    /// <summary>
    /// Gets or sets the time-to-live for user session states.
    /// </summary>
    /// <value>
    /// The TTL in hours. Must be between 1 and 48.
    /// Default value is 48 hours.
    /// </value>
    /// <remarks>
    /// After this period of inactivity, user data (selected stop, command context) is automatically cleared.
    /// Should be greater than <see cref="UpdateIntervalHours"/> to prevent state loss.
    /// </remarks>
    [Range(1, 48, ErrorMessage = "UserStateTtlHours must be between 1 and 48")]
    public int UserStateTtlHours { get; set; } = 48;

    /// <summary>
    /// Gets or sets the maximum number of requests allowed per user within the rate limit window.
    /// </summary>
    /// <value>
    /// Maximum request count. Must be between 1 and 20.
    /// Default value is 5 requests.
    /// </value>
    /// <remarks>
    /// This limit applies to all user interactions except admin commands.
    /// Prevents spam and abuse of the bot.
    /// </remarks>
    [Range(1, 20, ErrorMessage = "RateLimitMaxRequests must be between 1 and 20")]
    public int RateLimitMaxRequests { get; set; } = 5;

    /// <summary>
    /// Gets or sets the rate limit window duration in minutes.
    /// </summary>
    /// <value>
    /// The window duration in minutes. Must be between 1 and 10.
    /// Default value is 1 minute.
    /// </value>
    /// <remarks>
    /// Together with <see cref="RateLimitMaxRequests"/>, this defines the rate limit policy.
    /// For example, 5 requests per 1 minute.
    /// </remarks>
    [Range(1, 10, ErrorMessage = "RateLimitWindowMinutes must be between 1 and 10")]
    public int RateLimitWindowMinutes { get; set; } = 1;

    /// <summary>
    /// Gets or sets the maximum number of retry attempts for failed HTTP requests.
    /// </summary>
    /// <value>
    /// The retry count. Must be between 1 and 5.
    /// Default value is 3 retries.
    /// </value>
    /// <remarks>
    /// Retries are performed for transient errors such as network issues or server errors (5xx, 429).
    /// Uses exponential backoff with jitter for optimal performance.
    /// </remarks>
    [Range(1, 5, ErrorMessage = "MaxRetryCount must be between 1 and 5")]
    public int MaxRetryCount { get; set; } = 3;

    /// <summary>
    /// Gets or sets the base delay in seconds for retry backoff calculations.
    /// </summary>
    /// <value>
    /// The base delay in seconds. Must be between 1 and 60.
    /// Default value is 2 seconds.
    /// </value>
    /// <remarks>
    /// The actual delay is calculated as: baseDelaySeconds * 2^(retryAttempt - 1).
    /// For example, with BaseDelay = 2s: 2s, 4s, 8s, 16s...
    /// </remarks>
    [Range(1, 60, ErrorMessage = "RetryBaseDelaySeconds must be between 1 and 60")]
    public int RetryBaseDelaySeconds { get; set; } = 2;

    /// <summary>
    /// Gets or sets the timeout in seconds for HTTP requests.
    /// </summary>
    /// <value>
    /// The timeout duration in seconds. Must be between 5 and 120.
    /// Default value is 30 seconds.
    /// </value>
    /// <remarks>
    /// Applies to all HTTP requests including schedule loading and API calls.
    /// Should be high enough to accommodate slow networks or large payloads.
    /// </remarks>
    [Range(5, 120, ErrorMessage = "TimeoutSeconds must be between 5 and 120")]
    public int TimeoutSeconds { get; set; } = 30;

    private string? _timeZoneId;

    /// <summary>
    /// Gets or sets the IANA timezone identifier used to compute the bot's "current time".
    /// </summary>
    /// <value>
    /// A valid IANA timezone identifier (e.g., "Europe/Minsk"). Defaults to "Europe/Minsk".
    /// </value>
    /// <remarks>
    /// The schedule JSON contains wall-clock times in the users' local timezone. This
    /// setting ensures the bot compares those times against the correct local "now"
    /// regardless of the server/container timezone (which is often UTC). On Linux the
    /// value can also be overridden at the container level with the <c>TZ</c> environment
    /// variable; when <c>TZ</c> is set, it takes precedence over this setting.
    /// </remarks>
    public string TimeZoneId
    {
        get => _timeZoneId ?? "Europe/Minsk";
        set => _timeZoneId = value ?? string.Empty;
    }

    /// <summary>
    /// Gets a value indicating whether automatic update is enabled.
    /// </summary>
    /// <value>
    /// <c>true</c> if <see cref="UpdateIntervalHours"/> is greater than 0; otherwise, <c>false</c>.
    /// </value>
    /// <remarks>
    /// This property is not stored in configuration and is computed at runtime.
    /// Used internally to determine if the background refresh service should run.
    /// </remarks>
    public bool AutoUpdateEnabled => UpdateIntervalHours > 0;
}
