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
    private readonly long[] _adminUserIds = settings.Value.AdminUserIds ?? Array.Empty<long>();
    private readonly ILogger<CommandDispatcher> _logger = logger;

    // Кэшируем клавиатуру
    private static readonly ReplyKeyboardMarkup MainKeyboard = new([
        [BotCommands.Nearest, BotCommands.Today],
        [BotCommands.Week, BotCommands.ChangeStop],
        [BotCommands.ReportToAdmin, BotCommands.Back]
    ])
    { ResizeKeyboard = true };

    private bool IsAdmin(long userId) => _adminUserIds.Contains(userId);

    public async Task DispatchCommandAsync(long chatId, long userId, string command, string? args)
    {
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

    public async Task HandleCallbackAsync(CallbackQuery callbackQuery)
    {
        if (callbackQuery is null || string.IsNullOrEmpty(callbackQuery.Data))
        {
            _logger.LogWarning("Callback with null or empty data");
            return;
        }

        var userId = callbackQuery.From.Id;

        // Проверяем наличие чата
        if (callbackQuery.Message?.Chat?.Id is null or 0)
        {
            _logger.LogWarning("Callback without chat (UserId: {UserId})", userId);
            await _botClient.AnswerCallbackQuery(
                callbackQuery.Id,
                "⚠️ CHAT NOT FOUND. USE /start IN PRIVATE CHAT.");
            return;
        }

        var chatId = callbackQuery.Message.Chat.Id;
        var data = callbackQuery.Data;

        // Отвечаем на callback (убираем "часики")
        await _botClient.AnswerCallbackQuery(callbackQuery.Id);

        // Проверяем сессию
        if (!_userStateService.TryGet(userId, out _))
        {
            await _sender.SendAsync(chatId, "⛔ SESSION EXPIRED. USE /start.");
            return;
        }

        // Обработка системных callback
        if (data == BotCommands.AdminRefresh)
        {
            await HandleAdminRefreshAsync(chatId, userId);
            return;
        }

        // Парсинг stopId
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

        var stopKeyboard = new InlineKeyboardMarkup(
            allStops.Select(stop => InlineKeyboardButton.WithCallbackData(stop.Name, stop.Id)).ToArray());

        if (_userStateService.TryGet(userId, out var state))
            state.SelectedStopId = string.Empty;

        await _sender.SendAsync(chatId, BotMessages.Welcome, replyMarkup: stopKeyboard);
    }

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

            var keyboard = new InlineKeyboardMarkup(
                stops.Select(stop => InlineKeyboardButton.WithCallbackData(stop.Name, stop.Id)).ToArray());

            await _sender.SendAsync(chatId, $"{BotMessages.UpdateSuccess} {stops.Count} STOPS.\n\nSELECT STOP:", replyMarkup: keyboard);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual update failed");
            await _sender.SendAsync(chatId, BotMessages.UpdateFailed);
        }
    }

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
                  $"• CACHED TRIPS: {_storage.CachedTripsCount}\n" +
                  $"• ACTIVE USERS: {_userStateService.GetActiveUsersCount()}";

        await _sender.SendAsync(chatId, msg, ParseMode.Markdown);
    }

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
            var fullName = string.IsNullOrEmpty(state.FirstName) ? userId.ToString() : $"{state.FirstName} {state.LastName}".Trim();
            var username = string.IsNullOrEmpty(state.Username) ? "NO_USERNAME" : $"@{state.Username}";

            var adminMsg = $"📨 *NEW REPORT*\n\nUSER: {TimeHelper.EscapeMarkdown(fullName)}\nID: {userId}\nUSERNAME: {TimeHelper.EscapeMarkdown(username)}\n\n*MESSAGE:*\n{TimeHelper.EscapeMarkdown(reportText)}";

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

    private async Task ShowReportInstructionsAsync(long chatId)
    {
        if (_adminUserIds.Length == 0)
        {
            await _sender.SendAsync(chatId, "⚠️ NO ADMINS CONFIGURED. CONTACT SUPPORT.");
            return;
        }

        var firstAdminId = _adminUserIds[0];
        await _sender.SendAsync(
            chatId,
            BotMessages.ReportInstructions($"tg://user?id={firstAdminId}"),
            ParseMode.Markdown);
    }
}