namespace MaybeComingBot.Constants;

/// <summary>
/// Contains constant string values for all bot commands, buttons, and callback data.
/// </summary>
/// <remarks>
/// This class provides a centralized location for all command-related strings,
/// ensuring consistency and reducing magic strings throughout the codebase.
/// Commands are organized into three categories: slash commands, reply keyboard buttons,
/// and inline callback data.
/// </remarks>
public static class BotCommands
{
    /// <summary>
    /// Starts the bot interaction and presents the stop selection menu.
    /// </summary>
    /// <value>The command string "/start".</value>
    public const string Start = "/start";

    /// <summary>
    /// Forces an immediate refresh of the schedule data (admin only).
    /// </summary>
    /// <value>The command string "/update".</value>
    public const string Update = "/update";

    /// <summary>
    /// Displays the help menu with all available commands and buttons.
    /// </summary>
    /// <value>The command string "/help".</value>
    public const string Help = "/help";

    /// <summary>
    /// Sends a bug report or issue to the bot administrators.
    /// </summary>
    /// <value>The command string "/report".</value>
    public const string Report = "/report";

    /// <summary>
    /// Displays the current bot status and health information (admin only).
    /// </summary>
    /// <value>The command string "/status".</value>
    public const string Status = "/status";

    /// <summary>
    /// Cancels the current operation and clears the user's state.
    /// </summary>
    /// <value>The command string "/cancel".</value>
    public const string Cancel = "/cancel";

    /// <summary>
    /// Canonical command token for showing the nearest upcoming trip.
    /// Used for dispatch matching; not displayed directly.
    /// </summary>
    public const string Nearest = "Nearest";

    /// <summary>
    /// Display label for the nearest-trip button.
    /// </summary>
    /// <remarks>Includes a bus emoji for visual recognition.</remarks>
    public const string NearestLabel = "🚌 Nearest";

    /// <summary>
    /// Canonical command token for showing today's full schedule.
    /// Used for dispatch matching; not displayed directly.
    /// </summary>
    public const string Today = "Today";

    /// <summary>
    /// Display label for the today's-schedule button.
    /// </summary>
    /// <remarks>Includes a calendar emoji for visual recognition.</remarks>
    public const string TodayLabel = "📅 Today";

    /// <summary>
    /// Canonical command token for showing the weekly schedule.
    /// Used for dispatch matching; not displayed directly.
    /// </summary>
    public const string Week = "Week";

    /// <summary>
    /// Display label for the weekly-schedule button.
    /// </summary>
    /// <remarks>Includes a calendar emoji for visual recognition.</remarks>
    public const string WeekLabel = "🗓 Week";

    /// <summary>
    /// Canonical command token for changing the currently selected stop.
    /// Used for dispatch matching; not displayed directly.
    /// </summary>
    public const string ChangeStop = "Change stop";

    /// <summary>
    /// Display label for the change-stop button.
    /// </summary>
    /// <remarks>Includes a swap emoji for visual recognition.</remarks>
    public const string ChangeStopLabel = "🔄 Change stop";

    /// <summary>
    /// Canonical command token for reporting an issue to administrators.
    /// Used for dispatch matching; not displayed directly.
    /// </summary>
    public const string ReportToAdmin = "Report bug";

    /// <summary>
    /// Display label for the report-bug button.
    /// </summary>
    /// <remarks>Includes an envelope emoji for visual recognition.</remarks>
    public const string ReportToAdminLabel = "📨 Report bug";

    /// <summary>
    /// Canonical command token for returning to the main menu.
    /// Used for dispatch matching; not displayed directly.
    /// </summary>
    public const string Back = "Back";

    /// <summary>
    /// Display label for the back button.
    /// </summary>
    /// <remarks>Includes a back arrow emoji for visual recognition.</remarks>
    public const string BackLabel = "🔙 Back";

    /// <summary>
    /// Button label for cancelling the current operation.
    /// </summary>
    /// <remarks>Includes a cross mark emoji for visual recognition.</remarks>
    public const string CancelButton = "❌ Cancel";

    /// <summary>
    /// Callback data for triggering an admin refresh from an inline button.
    /// </summary>
    /// <remarks>
    /// This value is sent as the callback data when admin clicks the refresh
    /// button in the schedule loading screen.
    /// </remarks>
    public const string AdminRefresh = "admin_refresh";

    /// <summary>
    /// Display label for the admin refresh button.
    /// </summary>
    /// <remarks>Includes a refresh emoji for visual recognition.</remarks>
    public const string AdminRefreshLabel = "🔄 Refresh";
}