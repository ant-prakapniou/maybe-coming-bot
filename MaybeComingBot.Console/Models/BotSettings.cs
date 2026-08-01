using System.ComponentModel.DataAnnotations;

namespace MaybeComingBot.Models;

public class BotSettings
{
    [Required(ErrorMessage = "BotToken is required")]
    [MinLength(10, ErrorMessage = "BotToken must be at least 10 characters")]
    public string BotToken { get; set; } = string.Empty;

    [Required(ErrorMessage = "ScheduleUrl is required")]
    [Url(ErrorMessage = "ScheduleUrl must be a valid URL")]
    public string ScheduleUrl { get; set; } = string.Empty;

    [Required(ErrorMessage = "At least one AdminUserId is required")]
    [MinLength(1, ErrorMessage = "At least one admin user ID must be specified")]
    public long[] AdminUserIds { get; set; } = [];

    [Range(0, 168, ErrorMessage = "UpdateIntervalHours must be between 0 and 168 (7 days)")]
    public int UpdateIntervalHours { get; set; } = 24;

    [Range(1, 48, ErrorMessage = "UserStateTtlHours must be between 1 and 48")]
    public int UserStateTtlHours { get; set; } = 48;

    [Range(1, 20, ErrorMessage = "RateLimitMaxRequests must be between 1 and 20")]
    public int RateLimitMaxRequests { get; set; } = 5;

    [Range(1, 10, ErrorMessage = "RateLimitWindowMinutes must be between 1 and 10")]
    public int RateLimitWindowMinutes { get; set; } = 1;

    [Range(1, 5, ErrorMessage = "MaxRetryCount must be between 1 and 5")]
    public int MaxRetryCount { get; set; } = 3;

    [Range(1, 60, ErrorMessage = "RetryBaseDelaySeconds must be between 1 and 60")]
    public int RetryBaseDelaySeconds { get; set; } = 2;

    // ✅ Добавляем только это (новое свойство)
    [Range(5, 120, ErrorMessage = "TimeoutSeconds must be between 5 and 120")]
    public int TimeoutSeconds { get; set; } = 30;
}