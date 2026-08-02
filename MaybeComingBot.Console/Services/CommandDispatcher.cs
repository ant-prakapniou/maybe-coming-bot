﻿using MaybeComingBot.Abstractions;
using MaybeComingBot.Constants;
using MaybeComingBot.Models;
using MaybeComingBot.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
    IScheduleStorage storage,
    IUserStateService userStateService,
    IScheduleRefreshService refreshService,
    ScheduleFormatter formatter,
    StopSelectionGuard guard,
    ITelegramSender sender,
    IOptions<BotSettings> settings,
    ILogger<CommandDispatcher> logger)
{
    private readonly IScheduleStorage _storage = storage;
    private readonly IUserStateService _userStateService = userStateService;
    private readonly IScheduleRefreshService _refreshService = refreshService;
    private readonly ScheduleFormatter _formatter = formatter;
    private readonly StopSelectionGuard _guard = guard;
    private readonly ITelegramSender _sender = sender;
    private readonly HashSet<long> _adminUserIds = [.. settings?.Value?.AdminUserIds ?? []];
    private readonly string _supportUsername = settings?.Value?.SupportUsername ?? string.Empty;
    private readonly ILogger<CommandDispatcher> _logger = logger;

    /// <summary>
    /// Main keyboard displayed after stop selection.
    /// </summary>
    private static readonly ReplyKeyboardMarkup MainKeyboard = new(
    [
        [BotCommands.NearestLabel, BotCommands.TodayLabel],
        [BotCommands.WeekLabel, BotCommands.ChangeStopLabel],
        [BotCommands.ReportToAdminLabel, BotCommands.BackLabel]
    ])
    { ResizeKeyboard = true };

    /// <summary>
    /// Checks if a user is an administrator.
    /// </summary>
    /// <param name="userId">The Telegram user ID to check.</param>
    /// <returns><c>true</c> if the user is an admin; otherwise, <c>false</c>.</returns>
    private bool IsAdmin(long userId) => userId > 0 && _adminUserIds.Contains(userId);

    /// <summary>
    /// Normalizes a tapped keyboard label to its canonical command token.
    /// </summary>
    /// <param name="command">The raw command or button text.</param>
    /// <returns>
    /// The canonical command token (e.g., "Nearest", "Report bug"). Emoji prefixes
    /// from display labels are stripped so manually typed text still matches.
    /// </returns>
    private static string NormalizeCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return command ?? string.Empty;

        return command switch
        {
            BotCommands.NearestLabel => BotCommands.Nearest,
            BotCommands.TodayLabel => BotCommands.Today,
            BotCommands.WeekLabel => BotCommands.Week,
            BotCommands.ChangeStopLabel => BotCommands.ChangeStop,
            BotCommands.ReportToAdminLabel => BotCommands.ReportToAdmin,
            BotCommands.BackLabel => BotCommands.Back,
            _ => command.Trim()
        };
    }

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
    /// <item><description>Normalizes emoji-prefixed button labels to canonical tokens</description></item>
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
                await _sender.SendAsync(chatId, BotMessages.Help(_supportUsername), parseMode: ParseMode.Markdown);
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
        }

        // Normalize emoji-prefixed keyboard labels to canonical command tokens.
        command = NormalizeCommand(command);

        // These commands are available even without an active session/selected stop.
        switch (command)
        {
            case BotCommands.ReportToAdmin:
                await ShowReportInstructionsAsync(chatId);
                return;

            case BotCommands.ChangeStop:
            case BotCommands.Back:
                await HandleStartAsync(chatId, userId);
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
            await _sender.AnswerCallbackAsync(
                callbackQuery.Id,
                BotMessages.CallbackNoChat,
                showAlert: true);
            return;
        }

        var chatId = message.Chat.Id;
        var data = callbackQuery.Data;

        // Answer callback to remove "loading" indicator
        await _sender.AnswerCallbackAsync(callbackQuery.Id);

        // Check session
        if (!_userStateService.TryGet(userId, out _))
        {
            await _sender.SendAsync(chatId, BotMessages.SessionExpiredRestart);
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
        if (parts.Length is 0)
        {
            _logger.LogWarning("Invalid callback data format: {Data}", data);
            await _sender.SendAsync(chatId, BotMessages.InvalidCallbackData);
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

        if (allStops.Count is 0)
        {
            if (IsAdmin(userId))
            {
                var adminKeyboard = new InlineKeyboardMarkup(
                    InlineKeyboardButton.WithCallbackData(BotCommands.AdminRefreshLabel, BotCommands.AdminRefresh));
                await _sender.SendAsync(chatId, BotMessages.ScheduleNotLoaded, replyMarkup: adminKeyboard);
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

            if (stops.Count is 0)
            {
                await _sender.SendAsync(chatId, BotMessages.UpdatedButNoStops);
                return;
            }

            var keyboard = CreateStopKeyboard(stops);
            await _sender.SendAsync(
                chatId,
                $"{BotMessages.UpdateSuccess}\n\n{stops.Count} stops loaded.\n\n👇 Choose a stop:",
                replyMarkup: keyboard);
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

        var msg = $"📊 *Bot Status*\n\n" +
                  $"• Schedule: {_refreshService.GetStatus()}\n" +
                  $"• Refresh: {_refreshService.GetLastAttemptInfo()}\n" +
                  $"• Stops loaded: {_storage.StopCount}\n" +
                  $"• Active users: {_userStateService.GetActiveUsersCount()}";

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
            // The username is intentionally NOT markdown-escaped so Telegram renders
            // "@part1_part2" as a tappable mention instead of "@part1\_part2".
            var adminMsg = $"📨 *NEW REPORT*\n\n" +
                           $"USER: {TimeHelper.EscapeMarkdown(fullName)}\n" +
                           $"ID: {userId}\n" +
                           $"USERNAME: {username}\n\n" +
                           $"*MESSAGE:*\n{escapedReport}";

            foreach (var adminId in _adminUserIds)
            {
                try
                {
                    await _sender.SendAsync(adminId, adminMsg, parseMode: ParseMode.Markdown);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send report to admin {AdminId}", adminId);
                }
            }
            await _sender.SendAsync(chatId, BotMessages.ReportSent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Report failed from {UserId}", userId);
            await _sender.SendAsync(chatId, BotMessages.ReportFailed);
        }
    }

    /// <summary>
    /// Shows report instructions to the user.
    /// </summary>
    /// <remarks>
    /// The admin is referenced by their configured public username (SupportUsername)
    /// to avoid exposing a numeric Telegram user ID. When no username is configured,
    /// no contact link is rendered — the user is guided to use /report instead.
    /// </remarks>
    private async Task ShowReportInstructionsAsync(long chatId)
    {
        if (_adminUserIds.Count is 0)
        {
            await _sender.SendAsync(chatId, BotMessages.NoAdminsConfigured);
            return;
        }

        string? adminLink = null;
        string? adminUsername = null;

        if (!string.IsNullOrWhiteSpace(_supportUsername))
        {
            adminUsername = _supportUsername;
            adminLink = $"https://t.me/{_supportUsername}";
        }

        await _sender.SendAsync(
            chatId,
            BotMessages.ReportInstructions(adminLink, adminUsername),
            ParseMode.Markdown);
    }

    /// <summary>
    /// Creates an inline keyboard with stop selection buttons.
    /// </summary>
    /// <param name="stops">The collection of stops to display.</param>
    /// <returns>An inline keyboard markup with stop buttons.</returns>
    private static InlineKeyboardMarkup CreateStopKeyboard(IEnumerable<Stop> stops)
        => new(stops.Select(stop => InlineKeyboardButton.WithCallbackData(stop.Name, stop.Id)).ToArray());
}

