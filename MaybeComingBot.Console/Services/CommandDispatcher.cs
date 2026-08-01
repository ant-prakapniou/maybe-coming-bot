using MaybeComingBot.Abstractions;
using MaybeComingBot.Constants;
using MaybeComingBot.Models;
using MaybeComingBot.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace MaybeComingBot.Services;

/// <summary>
/// Dispatches commands and callback queries to appropriate handlers.
/// </summary>
/// <remarks>
/// This class is responsible for routing user interactions to the correct handlers:
/// <list type="bullet">
/// <item><description>Slash commands (/start, /help, /report, etc.)</description></item>
/// <item><description>Reply keyboard button commands (Nearest, Today, Week, etc.)</description></item>
/// <item><description>Inline callback queries (stop selection, admin refresh)</description></item>
/// </list>
/// It also manages admin-only commands and report forwarding to administrators.
/// </remarks>
public class CommandDispatcher(
    ITelegramBotClient botClient,
    IScheduleStorage storage,
    IUserStateService userStateService,
    IScheduleRefreshService refreshService,
    ScheduleFormatter formatter,
    StopSelectionGuard guard,
    TelegramSender sender,
    IOptions<BotSettings> settings,
    ILogger<CommandDispatcher> logger)
{
    private readonly ITelegramBotClient _botClient = botClient;
    private readonly IScheduleStorage _storage = storage;
    private readonly IUserStateService _userStateService = userStateService;
    private readonly IScheduleRefreshService _refreshService = refreshService;
    private readonly ScheduleFormatter _formatter = formatter;
    private readonly StopSelectionGuard _guard = guard;
    private readonly TelegramSender _sender = sender;
    private readonly HashSet<long> _adminUserIds = new(settings?.Value?.AdminUserIds ?? Array.Empty<long>());
    private readonly ILogger<CommandDispatcher> _logger = logger;

    /// <summary>
    /// Main keyboard displayed after stop selection.
    /// </summary>
    private static readonly ReplyKeyboardMarkup MainKeyboard = new(new[]
    {
        new KeyboardButton[] { BotCommands.Nearest, BotCommands.Today },
        new KeyboardButton[] { BotCommands.Week, BotCommands.ChangeStop },
        new KeyboardButton[] { BotCommands.ReportToAdmin, BotCommands.Back }
    })
    { ResizeKeyboard = true };

    /// <summary>
    /// Checks if a user is an administrator.
    /// </summary>
    /// <param name="userId">The Telegram user ID to check.</param>
    /// <returns><c>true</c> if the user is an admin; otherwise, <c>false</c>.</returns>
    private bool IsAdmin(long userId) => userId > 0 && _adminUserIds.Contains(userId);

    /// <summary>
    /// Dispatches a command from a user message.
    /// </summary>
    /// <param name="chatId">The Telegram chat ID.</param>
    /// <param name="userId">The Telegram user ID.</param>
    /// <param name="command">The command string (e.g., "/start", "Nearest").</param>
    /// <param name="args">Optional command arguments.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// Command flow:
    /// <list type="number">
    /// <item><description>Handles cancel, start, help, admin commands</description></item>
    /// <item><description>Checks if user has a stop selected (except for special commands)</description></item>
    /// <item><description>Routes to appropriate formatter method</description></item>
    /// <item><description>Unknown commands show help prompt</description></item>
    /// </list>
    /// </remarks>
    public async Task DispatchCommandAsync(long chatId, long userId, string command, string? args)
    {
        if (chatId <= 0 || userId <= 0)
        {
            _logger.LogWarning("Invalid chatId or userId: Chat={ChatId}, User={UserId}", chatId, userId);
            return;
        }

        switch (command)
        {
            case BotCommands.Cancel:
            case BotCommands.CancelButton:
                _userStateService.Clear(userId);
                await _sender.SendAsync(chatId, BotMessages.CancelConfirmed);
                return;

            case BotCommands.Start:
                await HandleStartAsync(chatId, userId);
                return;

            case BotCommands.Help:
                await _sender.SendAsync(chatId, BotMessages.Help(), parseMode: ParseMode.Markdown);
                return;

            case BotCommands.Update:
                await HandleAdminRefreshAsync(chatId, userId);
                return;

            case BotCommands.Status:
                await HandleStatusAsync(chatId, userId);
                return;

            case BotCommands.Report:
                await HandleReportAsync(chatId, userId, args);
                return;

            case BotCommands.ReportToAdmin:
                await ShowReportInstructionsAsync(chatId);
                return;
        }

        var stop = await _guard.EnsureStopSelectedAsync(chatId, userId);
        if (stop is null)
            return;

        switch (command)
        {
            case BotCommands.Nearest:
                await _sender.SendAsync(chatId, _formatter.FormatNearest(stop), ParseMode.Markdown);
                return;

            case BotCommands.Today:
                await _sender.SendAsync(chatId, _formatter.FormatToday(stop), ParseMode.Markdown);
                return;

            case BotCommands.Week:
                await _sender.SendAsync(chatId, _formatter.FormatWeek(stop), ParseMode.Markdown);
                return;

            case BotCommands.ChangeStop:
            case BotCommands.Back:
                await HandleStartAsync(chatId, userId);
                return;
        }

        if (!string.IsNullOrEmpty(command))
        {
            _logger.LogWarning("Unknown command from {UserId}: {Command}", userId, command);
            await _sender.SendAsync(chatId, BotMessages.UnknownCommand);
        }
    }

    /// <summary>
    /// Handles inline callback queries from inline keyboards.
    /// </summary>
    /// <param name="callbackQuery">The callback query from Telegram.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// Callback flow:
    /// <list type="number">
    /// <item><description>Validates callback data and user session</description></item>
    /// <item><description>Answers callback to remove loading indicator</description></item>
    /// <item><description>Handles admin refresh callback</description></item>
    /// <item><description>Parses stop ID and updates user selection</description></item>
    /// <item><description>Shows main keyboard after stop selection</description></item>
    /// </list>
    /// </remarks>
    public async Task HandleCallbackAsync(CallbackQuery callbackQuery)
    {
        if (callbackQuery is null || string.IsNullOrEmpty(callbackQuery.Data))
        {
            _logger.LogWarning("Callback with null or empty data");
            return;
        }

        var userId = callbackQuery.From.Id;
        if (userId <= 0)
        {
            _logger.LogWarning("Callback with invalid user ID");
            return;
        }

        var message = callbackQuery.Message;
        if (message?.Chat?.Id is null or 0)
        {
            _logger.LogWarning("Callback without chat (UserId: {UserId})", userId);
            await _botClient.AnswerCallbackQuery(
                callbackQuery.Id,
                "⚠️ CHAT NOT FOUND. USE /start IN PRIVATE CHAT.");
            return;
        }

        var chatId = message.Chat.Id;
        var data = callbackQuery.Data;

        // Answer callback to remove "loading" indicator
        await _botClient.AnswerCallbackQuery(callbackQuery.Id);

        // Check session
        if (!_userStateService.TryGet(userId, out _))
        {
            await _sender.SendAsync(chatId, "⛔ SESSION EXPIRED. USE /start.");
            return;
        }

        // Handle system callbacks
        if (data == BotCommands.AdminRefresh)
        {
            await HandleAdminRefreshAsync(chatId, userId);
            return;
        }

        // Parse stop ID
        var parts = data.Split('|', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            _logger.LogWarning("Invalid callback data format: {Data}", data);
            await _sender.SendAsync(chatId, "⚠️ INVALID DATA. USE /start.");
            return;
        }

        var stopId = parts[0];
        var stop = _storage.GetStop(stopId);

        if (stop is null)
        {
            _logger.LogWarning("Stop not found for callback: {Data}", data);
            await _sender.SendAsync(chatId, BotMessages.StopNotFound);
            return;
        }

        _userStateService.SetStop(userId, stop.Id);
        await _sender.SendAsync(
            chatId,
            BotMessages.StopSelected(stop.Name),
            parseMode: ParseMode.Markdown,
            replyMarkup: MainKeyboard);
    }

    /// <summary>
    /// Handles the /start command by displaying stop selection keyboard.
    /// </summary>
    private async Task HandleStartAsync(long chatId, long userId)
    {
        var allStops = _storage.GetAllStops().ToList();

        if (allStops.Count == 0)
        {
            if (IsAdmin(userId))
            {
                var adminKeyboard = new InlineKeyboardMarkup(
                    InlineKeyboardButton.WithCallbackData("🔄 REFRESH", BotCommands.AdminRefresh));
                await _sender.SendAsync(chatId, "⚠️ SCHEDULE NOT LOADED. PRESS TO REFRESH:", replyMarkup: adminKeyboard);
                return;
            }

            await _sender.SendAsync(chatId, BotMessages.NoSchedule);
            return;
        }

        var stopKeyboard = CreateStopKeyboard(allStops);

        if (_userStateService.TryGet(userId, out var state))
            state.SelectedStopId = string.Empty;

        await _sender.SendAsync(chatId, BotMessages.Welcome, replyMarkup: stopKeyboard);
    }

    /// <summary>
    /// Handles admin refresh command (/update or callback).
    /// </summary>
    private async Task HandleAdminRefreshAsync(long chatId, long userId)
    {
        if (!IsAdmin(userId))
        {
            await _sender.SendAsync(chatId, BotMessages.NoRights);
            return;
        }

        _logger.LogInformation("Manual update triggered by {UserId}", userId);
        await _sender.SendAsync(chatId, BotMessages.UpdateLoading);

        try
        {
            await _refreshService.ForceRefreshAsync();
            var stops = _storage.GetAllStops().ToList();

            if (stops.Count == 0)
            {
                await _sender.SendAsync(chatId, "⚠️ UPDATED BUT NO STOPS.");
                return;
            }

            var keyboard = CreateStopKeyboard(stops);
            await _sender.SendAsync(chatId, $"{BotMessages.UpdateSuccess} {stops.Count} STOPS.\n\nSELECT STOP:", replyMarkup: keyboard);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual update failed");
            await _sender.SendAsync(chatId, BotMessages.UpdateFailed);
        }
    }

    /// <summary>
    /// Handles admin status command (/status).
    /// </summary>
    private async Task HandleStatusAsync(long chatId, long userId)
    {
        if (!IsAdmin(userId))
        {
            await _sender.SendAsync(chatId, BotMessages.NoRights);
            return;
        }

        var msg = $"📊 *BOT STATUS*\n\n" +
                  $"• {_refreshService.GetStatus()}\n" +
                  $"• {_refreshService.GetLastAttemptInfo()}\n" +
                  $"• STOPS: {_storage.StopCount}\n" +
                  $"• ACTIVE USERS: {_userStateService.GetActiveUsersCount()}";

        await _sender.SendAsync(chatId, msg, ParseMode.Markdown);
    }

    /// <summary>
    /// Handles user report submission (/report).
    /// </summary>
    private async Task HandleReportAsync(long chatId, long userId, string? reportText)
    {
        if (string.IsNullOrWhiteSpace(reportText))
        {
            await ShowReportInstructionsAsync(chatId);
            return;
        }

        try
        {
            var state = _userStateService.GetOrCreate(userId);
            var fullName = string.IsNullOrEmpty(state.FirstName)
                ? userId.ToString()
                : $"{state.FirstName} {state.LastName}".Trim();
            var username = string.IsNullOrEmpty(state.Username)
                ? "NO_USERNAME"
                : $"@{state.Username}";

            var escapedReport = TimeHelper.EscapeMarkdown(reportText);
            var adminMsg = $"📨 *NEW REPORT*\n\nUSER: {TimeHelper.EscapeMarkdown(fullName)}\nID: {userId}\nUSERNAME: {TimeHelper.EscapeMarkdown(username)}\n\n*MESSAGE:*\n{escapedReport}";

            foreach (var adminId in _adminUserIds)
            {
                try
                {
                    await _botClient.SendMessage(adminId, adminMsg, parseMode: ParseMode.Markdown);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send report to admin {AdminId}", adminId);
                }
            }
            await _sender.SendAsync(chatId, "✅ REPORT SENT. THANK YOU.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Report failed from {UserId}", userId);
            await _sender.SendAsync(chatId, "❌ SEND FAILED. RETRY LATER.");
        }
    }

    /// <summary>
    /// Shows report instructions to the user.
    /// </summary>
    private async Task ShowReportInstructionsAsync(long chatId)
    {
        if (_adminUserIds.Count == 0)
        {
            await _sender.SendAsync(chatId, "⚠️ NO ADMINS CONFIGURED. CONTACT SUPPORT.");
            return;
        }

        var firstAdminId = _adminUserIds.First();
        await _sender.SendAsync(
            chatId,
            BotMessages.ReportInstructions($"tg://user?id={firstAdminId}"),
            ParseMode.Markdown);
    }

    /// <summary>
    /// Creates an inline keyboard with stop selection buttons.
    /// </summary>
    /// <param name="stops">The collection of stops to display.</param>
    /// <returns>An inline keyboard markup with stop buttons.</returns>
    private static InlineKeyboardMarkup CreateStopKeyboard(IEnumerable<Stop> stops)
    {
        return new InlineKeyboardMarkup(
            stops.Select(stop => InlineKeyboardButton.WithCallbackData(stop.Name, stop.Id)).ToArray());
    }
}