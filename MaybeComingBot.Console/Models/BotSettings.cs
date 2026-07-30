using System.ComponentModel.DataAnnotations;

namespace MaybeComingBot.Models;

public class BotSettings
{
    [Required(ErrorMessage = "BotToken is required")]
    [MinLength(10, ErrorMessage = "BotToken must be at least 10 characters")]
    public string BotToken { get; set; } = string.Empty;

    [Required(ErrorMessage = "JsonUrl is required")]
    [Url(ErrorMessage = "JsonUrl must be a valid URL")]
    public string JsonUrl { get; set; } = string.Empty;

    [Required(ErrorMessage = "AdminUserId is required")]
    public long AdminUserId { get; set; }

    [Range(0, 168, ErrorMessage = "UpdateIntervalHours must be between 0 and 168 (7 days)")]
    public int UpdateIntervalHours { get; set; } = 24;

    [Range(1, 24, ErrorMessage = "UserStateTtlHours must be between 1 and 24")]
    public int UserStateTtlHours { get; set; } = 2;
}