namespace MaybeComingBot.Constants;

public static class BotMessages
{
    public const string Welcome = "🚌 SELECT STOP.";
    public const string StopNotFound = "⚠️ STOP NOT FOUND. USE /start.";
    public const string NoSchedule = "⚠️ SCHEDULE UNAVAILABLE. TRY /update.";
    public const string ChooseStopFirst = "SELECT STOP VIA /start FIRST.";
    public const string UpdateSuccess = "✅ SCHEDULE UPDATED.";
    public const string UpdateFailed = "❌ UPDATE FAILED. RETRY LATER.";
    public const string UpdateLoading = "⏳ FETCHING SCHEDULE...";
    public const string NoRights = "⛔ ACCESS DENIED.";
    public const string UnknownCommand = "UNKNOWN. USE /help.";
    public const string CancelConfirmed = "✅ ACTION CANCELLED. USE /start.";
    public const string TooManyRequests = "⛔ SLOW DOWN. RATE LIMIT EXCEEDED.";

    public static string StopSelected(string stopName) =>
        $"✅ STOP SET: *{stopName}*. USE BUTTONS:";

    public static string NoTripsToday(string stopName, DayOfWeek day) =>
        $"⛔ {stopName}: NO TRIPS ON {day}.";

    public static string NoTripsTomorrow(DayOfWeek day) =>
        $"⛔ NO MORE TRIPS TODAY. TOMORROW ({day}) ALSO EMPTY.";

    public static string NearestTomorrow(DayOfWeek day, string time) =>
        $"⛔ NO MORE TRIPS TODAY.\nNEXT: TOMORROW ({day}) AT {time}.";

    public static string ReportInstructions(string adminLink) =>
        $"📨 *REPORT TO ADMIN*\n\n" +
        $"FOUND A BUG? WRITE:\n" +
        $"[👤 CONTACT ADMIN]({adminLink})\n\n" +
        $"OR USE: `/report YOUR_TEXT`\n\n" +
        $"*REQUIRED:*\n" +
        $"• STOP NAME\n" +
        $"• TIME\n" +
        $"• ISSUE DESCRIPTION";

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
}