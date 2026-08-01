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
    /// Button label for showing the nearest upcoming trip.
    /// </summary>
    public const string Nearest = "Nearest";

    /// <summary>
    /// Button label for showing today's full schedule.
    /// </summary>
    public const string Today = "Today";

    /// <summary>
    /// Button label for showing the weekly schedule.
    /// </summary>
    public const string Week = "Week";

    /// <summary>
    /// Button label for changing the currently selected stop.
    /// </summary>
    public const string ChangeStop = "Change stop";

    /// <summary>
    /// Button label for reporting an issue to administrators.
    /// </summary>
    /// <remarks>Includes an envelope emoji for visual recognition.</remarks>
    public const string ReportToAdmin = "📨 Report bug";

    /// <summary>
    /// Button label for returning to the main menu.
    /// </summary>
    /// <remarks>Includes a back arrow emoji for visual recognition.</remarks>
    public const string Back = "🔙 Back";

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
}