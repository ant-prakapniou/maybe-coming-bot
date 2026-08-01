namespace MaybeComingBot.Constants;

public static class BotCommands
{
    // ============================================================
    // SLASH COMMANDS (начинаются с /)
    // ============================================================
    public const string Start = "/start";
    public const string Update = "/update";
    public const string Help = "/help";
    public const string Report = "/report";
    public const string Status = "/status";
    public const string Cancel = "/cancel";

    // ============================================================
    // REPLY KEYBOARD BUTTONS (текст на кнопках)
    // ============================================================
    public const string Nearest = "Nearest";
    public const string Today = "Today";
    public const string Week = "Week";
    public const string ChangeStop = "Change stop";
    public const string ReportToAdmin = "📨 Report bug";
    public const string Back = "🔙 Back";
    public const string CancelButton = "❌ Cancel";

    // ============================================================
    // INLINE CALLBACK DATA (для InlineKeyboardButton)
    // ============================================================
    public const string AdminRefresh = "admin_refresh";
}