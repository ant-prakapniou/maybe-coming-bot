using MaybeComingBot.Utils;

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
    public const string Welcome = "🚌 *SELECT STOP.*";

    /// <summary>Message shown when a stop is not found in the schedule.</summary>
    public const string StopNotFound = "⚠️ *STOP NOT FOUND.* USE /start.";

    /// <summary>Message shown when schedule data is not available.</summary>
    public const string NoSchedule = "⚠️ *SCHEDULE UNAVAILABLE.* TRY /update.";

    /// <summary>Prompt asking user to select a stop first.</summary>
    public const string ChooseStopFirst = "*SELECT STOP* VIA /start FIRST.";

    /// <summary>Confirmation message after successful schedule update.</summary>
    public const string UpdateSuccess = "✅ *SCHEDULE UPDATED.*";

    /// <summary>Error message when schedule update fails.</summary>
    public const string UpdateFailed = "❌ *UPDATE FAILED.* RETRY LATER.";

    /// <summary>Status message shown while schedule is being fetched.</summary>
    public const string UpdateLoading = "⏳ *FETCHING SCHEDULE...*";

    /// <summary>Message shown when user lacks administrative privileges.</summary>
    public const string NoRights = "⛔ *ACCESS DENIED.*";

    /// <summary>Message shown for unrecognized commands.</summary>
    public const string UnknownCommand = "⚠️ *UNKNOWN COMMAND.* USE /help.";

    /// <summary>Confirmation message after action cancellation.</summary>
    public const string CancelConfirmed = "✅ *ACTION CANCELLED.* USE /start.";

    /// <summary>Warning message when rate limit is exceeded.</summary>
    public const string TooManyRequests = "⛔ *SLOW DOWN.* RATE LIMIT EXCEEDED.";

    /// <summary>Template for no trips in the near future (7 days).</summary>
    /// <remarks>Placeholder {0} is the stop name.</remarks>
    public const string NoTripsInFuture = "🚌 *{0}*\n\n❌ *NO TRIPS IN THE NEAR FUTURE (7 DAYS).*";

    /// <summary>Message shown when user session has expired.</summary>
    public const string SessionExpired = "⏳ SESSION EXPIRED. PLEASE USE /start TO RESTART.";
    
    /// <summary>
    /// Formats a message confirming stop selection.
    /// </summary>
    /// <param name="stopName">The name of the selected stop.</param>
    /// <returns>A formatted confirmation message with the stop name.</returns>
    public static string StopSelected(string stopName) =>
        $"✅ *STOP SET:* {TimeHelper.EscapeMarkdown(stopName) ?? "UNKNOWN"}. USE BUTTONS:";

    /// <summary>
    /// Formats a message indicating no trips on a specific day.
    /// </summary>
    /// <param name="stopName">The name of the stop.</param>
    /// <param name="day">The day of the week with no trips.</param>
    /// <returns>A formatted message stating no trips on the given day.</returns>
    public static string NoTripsToday(string stopName, DayOfWeek day) =>
        $"⛔ {TimeHelper.EscapeMarkdown(stopName) ?? "STOP"}: *NO TRIPS ON {day}.*";

    /// <summary>
    /// Formats a message indicating no trips today or tomorrow.
    /// </summary>
    /// <param name="tomorrow">The day of the week for tomorrow.</param>
    /// <returns>A formatted message stating no trips for today or tomorrow.</returns>
    public static string NoTripsTodayAndTomorrow(DayOfWeek tomorrow) =>
        $"⛔ *NO TRIPS TODAY OR TOMORROW ({tomorrow}).*";

    /// <summary>
    /// Formats a message for the nearest trip on the next day.
    /// </summary>
    /// <param name="day">The day of the week for the next trip.</param>
    /// <param name="time">The departure time of the next trip.</param>
    /// <returns>A formatted message showing the next trip tomorrow.</returns>
    public static string NearestTomorrow(DayOfWeek day, string time) =>
        $"⛔ *NO MORE TRIPS TODAY.*\n*NEXT:* TOMORROW ({day}) AT {TimeHelper.EscapeMarkdown(time) ?? "UNKNOWN"}.";

    /// <summary>
    /// Formats instructions for submitting a bug report to administrators.
    /// </summary>
    /// <param name="adminLink">The Telegram link to contact an administrator.</param>
    /// <returns>A formatted message with report instructions and admin contact link.</returns>
    public static string ReportInstructions(string adminLink) =>
        $"📨 *REPORT TO ADMIN*\n\n" +
        $"FOUND A BUG? WRITE:\n" +
        $"[👤 CONTACT ADMIN]({adminLink})\n\n" +
        $"OR USE: `/report YOUR_TEXT`\n\n" +
        $"*REQUIRED:*\n" +
        $"• STOP NAME\n" +
        $"• TIME\n" +
        $"• ISSUE DESCRIPTION";

    /// <summary>
    /// Formats the help menu with all available commands and buttons.
    /// </summary>
    /// <returns>A formatted help message with command and button descriptions.</returns>
    public static string Help() =>
        "📖 *COMMANDS:*\n\n" +
        "/start — SELECT STOP\n" +
        "/update — REFRESH (ADMIN ONLY)\n" +
        "/report — SEND BUG REPORT\n" +
        "/status — BOT STATUS (ADMIN)\n" +
        "/cancel — ABORT ACTION\n" +
        "/help — THIS SCREEN\n\n" +
        "*BUTTONS:*\n" +
        "• *Nearest* — NEXT BUS\n" +
        "• *Today* — FULL DAY\n" +
        "• *Week* — 7 DAYS\n" +
        "• *Change stop* — RE-SELECT\n" +
        "• *📨 Report bug* — TO ADMIN\n" +
        "• *🔙 Back* — MAIN MENU";

    /// <summary>
    /// Formats an error message with a consistent prefix.
    /// </summary>
    /// <param name="message">The error description.</param>
    /// <returns>A formatted error message with ❌ prefix.</returns>
    public static string Error(string message) =>
        $"❌ {message}";

    /// <summary>
    /// Formats a success message with a consistent prefix.
    /// </summary>
    /// <param name="message">The success description.</param>
    /// <returns>A formatted success message with ✅ prefix.</returns>
    public static string Success(string message) =>
        $"✅ {message}";

    /// <summary>
    /// Formats an informational message with a consistent prefix.
    /// </summary>
    /// <param name="message">The informational content.</param>
    /// <returns>A formatted info message with ℹ️ prefix.</returns>
    public static string Info(string message) =>
        $"ℹ️ {message}";

    /// <summary>
    /// Formats a warning message with a consistent prefix.
    /// </summary>
    /// <param name="message">The warning description.</param>
    /// <returns>A formatted warning message with ⚠️ prefix.</returns>
    public static string Warning(string message) =>
        $"⚠️ {message}";
}