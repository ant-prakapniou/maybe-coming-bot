﻿using MaybeComingBot.Utils;

namespace MaybeComingBot.Constants;

/// <summary>
/// Contains all user-facing message templates and helper methods for the bot.
/// </summary>
/// <remarks>
/// This class provides a centralized location for all bot messages,
/// ensuring consistency and supporting multilingual format if needed.
/// Messages are formatted with Markdown for rich text display in Telegram.
/// </remarks>
public static class BotMessages
{
    /// <summary>Welcome message displayed when user starts the bot.</summary>
    public const string Welcome = "👋 *Welcome!*\n\nChoose a stop to see bus schedules.";

    /// <summary>Message shown when a stop is not found in the schedule.</summary>
    public const string StopNotFound = "⚠️ *Stop not found.*\n\nPlease use /start to choose a stop again.";

    /// <summary>Message shown when schedule data is not available.</summary>
    public const string NoSchedule = "😔 *Schedule is currently unavailable.*\n\nPlease try again later.";

    /// <summary>Prompt asking user to select a stop first.</summary>
    public const string ChooseStopFirst = "👇 *Please select a stop first.*\n\nUse /start to open the stop menu.";

    /// <summary>Confirmation message after successful schedule update.</summary>
    public const string UpdateSuccess = "✅ *Schedule updated successfully.*";

    /// <summary>Error message when schedule update fails.</summary>
    public const string UpdateFailed = "❌ *Update failed.*\n\nPlease try again in a few minutes.";

    /// <summary>Status message shown while schedule is being fetched.</summary>
    public const string UpdateLoading = "⏳ *Fetching the latest schedule...*";

    /// <summary>Message shown when user lacks administrative privileges.</summary>
    public const string NoRights = "⛔ *Access denied.*\n\nYou don't have permission to use this command.";

    /// <summary>Message shown for unrecognized commands.</summary>
    public const string UnknownCommand = "🤔 *Unknown command.*\n\nUse /help to see what I can do.";

    /// <summary>Confirmation message after action cancellation.</summary>
    public const string CancelConfirmed = "✅ *Action cancelled.*\n\nUse /start to begin again.";

    /// <summary>Warning message when rate limit is exceeded.</summary>
    public const string TooManyRequests = "🐢 *Slow down a bit!*\n\nYou're sending messages too quickly. Please wait a moment.";

    /// <summary>Message shown when a callback query has no associated chat.</summary>
    public const string CallbackNoChat = "⚠️ Chat not found. Please use /start in a private chat.";

    /// <summary>Message shown when the user's session has expired.</summary>
    public const string SessionExpiredRestart = "⏰ *Your session has expired.*\n\nUse /start to continue.";

    /// <summary>Message shown when callback data is malformed.</summary>
    public const string InvalidCallbackData = "⚠️ *Invalid selection.*\n\nPlease use /start to try again.";

    /// <summary>Message shown to admins when the schedule is not loaded yet.</summary>
    public const string ScheduleNotLoaded = "⚠️ *Schedule is not loaded yet.*\n\nPress the button below to fetch it now:";

    /// <summary>Message shown to admins after a successful refresh with no stops.</summary>
    public const string UpdatedButNoStops = "⚠️ *Updated, but no stops were found.*\n\nPlease check the schedule data source.";

    /// <summary>Message shown when a user report is sent successfully.</summary>
    public const string ReportSent = "✅ *Report sent.*\n\nThank you for your feedback! We'll take a look soon.";

    /// <summary>Message shown when a user report fails to send.</summary>
    public const string ReportFailed = "❌ *Could not send the report.*\n\nPlease try again in a few minutes.";

    /// <summary>Message shown when no admins are configured for reporting.</summary>
    public const string NoAdminsConfigured = "⚠️ *No support is configured right now.*\n\nPlease try again later.";

    /// <summary>Message shown when the selected stop has no schedule data.</summary>
    public const string StopHasNoSchedule = "⚠️ *No schedule is available for this stop.*\n\nPlease try another stop.";

    /// <summary>Header used in the report instructions block.</summary>
    public const string ReportHeader = "📨 *Report a bug or issue*";

    /// <summary>
    /// Formats a message confirming stop selection.
    /// </summary>
    /// <param name="stopName">The name of the selected stop.</param>
    /// <returns>A formatted confirmation message with the stop name.</returns>
    public static string StopSelected(string stopName) =>
        $"✅ *Stop selected:* {TimeHelper.EscapeMarkdown(stopName) ?? "Unknown"}.\n\nWhat would you like to see?";

    /// <summary>
    /// Formats a message indicating no trips on a specific day.
    /// </summary>
    /// <param name="stopName">The name of the stop.</param>
    /// <param name="day">The day of the week with no trips.</param>
    /// <returns>A formatted message stating no trips on the given day.</returns>
    public static string NoTripsToday(string stopName, DayOfWeek day) =>
        $"🚫 {TimeHelper.EscapeMarkdown(stopName) ?? "This stop"}: *no trips on {day}.*";

    /// <summary>
    /// Formats instructions for submitting a bug report to administrators.
    /// </summary>
    /// <param name="adminLink">An optional Telegram contact link (e.g., "https://t.me/support"). May be null.</param>
    /// <param name="adminUsername">An optional display username (e.g., "support") for the contact link. May be null.</param>
    /// <returns>A formatted message with report instructions and an optional admin contact link.</returns>
    public static string ReportInstructions(string? adminLink, string? adminUsername = null)
    {
        var contactLine = string.Empty;

        if (!string.IsNullOrWhiteSpace(adminLink))
        {
            var label = string.IsNullOrWhiteSpace(adminUsername)
                ? "Contact admin"
                : $"@{adminUsername}";
            contactLine = $"[👤 {label}]({adminLink})\n\n";
        }

        return $"{ReportHeader}\n\n" +
               "Found a bug or have a suggestion? Send it straight to the support team:\n\n" +
               $"• Use `/report YOUR_TEXT` — e.g., `/report Bus 42 was late at 08:15`\n\n" +
               $"{contactLine}" +
               "*Tip:* include the stop name, time, and a short description so we can fix it faster.";
    }

    /// <summary>
    /// Formats the help menu with all available commands and buttons.
    /// </summary>
    /// <param name="adminUsername">
    /// An optional public Telegram username (without the leading "@") used to render an
    /// admin contact link (e.g., "support"). When omitted, no contact link is shown.
    /// </param>
    /// <returns>A formatted help message with command and button descriptions.</returns>
    public static string Help(string? adminUsername = null)
    {
        var contactLine = string.Empty;

        if (!string.IsNullOrWhiteSpace(adminUsername))
        {
            contactLine = $"\n\n👤 *Contact support:* [@{adminUsername}](https://t.me/{adminUsername})";
        }

        return "📖 *Help & commands*\n\n" +
               "*/start* — Choose a stop\n" +
               "*/report* — Send feedback to support\n" +
               "*/cancel* — Cancel the current action\n" +
               "*/help* — Show this screen\n\n" +
               "*Admin commands*\n" +
               "*/update* — Refresh the schedule\n" +
               "*/status* — Bot health and statistics\n\n" +
               "*Buttons*\n" +
               $"• {BotCommands.NearestLabel} — next departure\n" +
               $"• {BotCommands.TodayLabel} — full schedule for today\n" +
               $"• {BotCommands.WeekLabel} — 7-day overview\n" +
               $"• {BotCommands.ChangeStopLabel} — pick a different stop\n" +
               $"• {BotCommands.ReportToAdminLabel} — report a problem\n" +
               $"• {BotCommands.BackLabel} — back to the stop menu" +
               contactLine;
    }
}

