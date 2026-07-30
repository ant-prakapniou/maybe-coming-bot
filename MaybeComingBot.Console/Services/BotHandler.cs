using MaybeComingBot.Abstractions;
using MaybeComingBot.Constants;
using MaybeComingBot.Models;
using MaybeComingBot.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace MaybeComingBot.Services;

public class BotHandler(
    ITelegramBotClient botClient,
    ScheduleStorage storage,
    ScheduleRefreshService refreshService,
    IOptions<BotSettings> settings,
    IUserStateService userStateService,
    RateLimiter rateLimiter,
    ILogger<BotHandler> logger)
{
    private readonly ITelegramBotClient _botClient = botClient;
    private readonly ScheduleStorage _storage = storage;
    private readonly ScheduleRefreshService _refreshService = refreshService;
    private readonly long _adminUserId = settings.Value.AdminUserId;
    private readonly IUserStateService _userStateService = userStateService;
    private readonly RateLimiter _rateLimiter = rateLimiter;
    private readonly ILogger<BotHandler> _logger = logger;
    private const string StopPrefix = "stop_";

    public async Task HandleUpdateAsync(Update update)
    {
        try
        {
            var userId = GetUserId(update);
            var chatId = GetChatId(update);

            if (update.Type == UpdateType.EditedMessage)
            {
                var editedMessage = update.EditedMessage;
                if (editedMessage is not null && !string.IsNullOrWhiteSpace(editedMessage.Text))
                {
                    _logger.LogInformation("User {UserId} edited message: {Text}",
                        editedMessage.From?.Id, editedMessage.Text);
                }
                return;
            }

            if (userId > 0)
            {
                UpdateUserActivity(userId);
            }

            if (update.Type == UpdateType.CallbackQuery)
            {
                await HandleCallbackQuery(update.CallbackQuery!);
                return;
            }

            if (update.Type != UpdateType.Message)
                return;

            var message = update.Message;
            if (message is null || string.IsNullOrWhiteSpace(message.Text))
                return;

            var text = message.Text.Trim();

            if (message.From is not null)
            {
                _userStateService.SetUserInfo(
                    userId,
                    message.From.FirstName ?? string.Empty,
                    message.From.LastName,
                    message.From.Username);
            }

            if (!await _rateLimiter.IsAllowedAsync(userId))
            {
                await SendWithRetryAsync(chatId, "Слишком много запросов. Подождите немного.");
                return;
            }

            _logger.LogInformation("User {UserId} command: {Command}", userId, text);

            if (text == BotCommands.Cancel || text == BotCommands.CancelButton)
            {
                _userStateService.Clear(userId);
                await SendWithRetryAsync(chatId, "✅ Действие отменено. Используйте /start для начала.");
                return;
            }

            if (text.StartsWith(BotCommands.Start))
            {
                await HandleStartCommand(chatId, userId);
                return;
            }

            if (text.StartsWith(BotCommands.Update))
            {
                await HandleUpdateCommand(chatId, userId);
                return;
            }

            if (text.StartsWith(BotCommands.Help))
            {
                await HandleHelpCommand(chatId);
                return;
            }

            if (text.StartsWith(BotCommands.Report))
            {
                var reportText = text.Length > 7 ? text[8..].Trim() : null;
                await HandleReportCommand(chatId, userId, reportText);
                return;
            }

            if (text.StartsWith(BotCommands.Status))
            {
                await HandleStatusCommand(chatId, userId);
                return;
            }

            if (text == BotCommands.ReportToAdmin)
            {
                await ShowReportInstructions(chatId);
                return;
            }

            await HandleTextMessage(chatId, userId, text);
        }
        catch (ApiRequestException apiEx)
        {
            await HandleTelegramApiException(apiEx);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling update");
            throw;
        }
    }

    #region Private Methods

    private static long GetUserId(Update update)
    {
        if (update is null)
            return 0;

        return update.Message?.From?.Id ?? update.CallbackQuery?.From?.Id ?? 0;
    }

    private static long GetChatId(Update update)
    {
        if (update is null)
            return 0;

        return update.Message?.Chat?.Id ?? update.CallbackQuery?.Message?.Chat?.Id ?? 0;
    }

    private void UpdateUserActivity(long userId)
    {
        _userStateService.UpdateActivity(userId);
    }

    private bool IsAdmin(long userId) => userId == _adminUserId;

    private async Task HandleCallbackQuery(CallbackQuery callbackQuery)
    {
        if (callbackQuery is null || string.IsNullOrEmpty(callbackQuery.Data))
            return;

        var chatId = callbackQuery.Message?.Chat?.Id ?? 0;
        var userId = callbackQuery.From.Id;
        var data = callbackQuery.Data;

        _logger.LogInformation("Callback from user {UserId}: {Data}", userId, data);

        await _botClient.AnswerCallbackQuery(callbackQuery.Id);

        if (data == "admin_refresh")
        {
            if (!IsAdmin(userId))
            {
                await SendWithRetryAsync(chatId, BotMessages.NoRights);
                return;
            }

            await SendWithRetryAsync(chatId, "⏳ Обновляю расписание...");

            try
            {
                await _refreshService.ForceRefreshAsync();

                var stops = _storage.GetAllStops().ToList();
                _logger.LogInformation("After refresh: GetAllStops.Count={Count}", stops.Count);

                if (stops.Count == 0)
                {
                    await SendWithRetryAsync(chatId, "⚠️ Расписание обновлено, но остановок нет.");
                    return;
                }

                var refreshKeyboard = new InlineKeyboardMarkup(
                    stops.Select(stop =>
                        InlineKeyboardButton.WithCallbackData(stop.Name, $"{StopPrefix}{stop.Id}")
                    ).ToArray()
                );

                await SendWithRetryAsync(
                    chatId,
                    $"✅ Расписание обновлено! ({stops.Count} остановок)\n\nВыберите остановку:",
                    replyMarkup: refreshKeyboard
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Admin refresh failed for user {UserId}", userId);
                await SendWithRetryAsync(chatId, "❌ Не удалось обновить расписание.");
            }
            return;
        }

        var stop = FindStop(data);
        if (stop is null)
        {
            await SendWithRetryAsync(
                chatId,
                $"{BotMessages.StopNotFound} (data: {data})");
            return;
        }

        _userStateService.SetStop(userId, stop.Id);

        var mainKeyboard = GetMainKeyboard();

        await SendWithRetryAsync(
            chatId,
            BotMessages.StopSelected(stop.Name),
            parseMode: ParseMode.Markdown,
            replyMarkup: mainKeyboard
        );
    }

    private Stop? FindStop(string data)
    {
        var stop = _storage.GetStop(data);
        if (stop is not null)
            return stop;

        if (data.StartsWith(StopPrefix))
        {
            var stopId = data[StopPrefix.Length..];
            return _storage.GetStop(stopId);
        }

        return _storage.GetStop(StopPrefix + data);
    }

    private static ReplyKeyboardMarkup GetMainKeyboard()
    {
        return new ReplyKeyboardMarkup(
        [
            [BotCommands.Nearest, BotCommands.Today],
            [BotCommands.Week, BotCommands.ChangeStop],
            [BotCommands.ReportToAdmin, BotCommands.Back]
        ])
        {
            ResizeKeyboard = true
        };
    }

    private async Task HandleStartCommand(long chatId, long userId)
    {
        _logger.LogInformation("📌 Start command from user {UserId}", userId);

        _userStateService.Clear(userId);

        var hasData = _storage.HasData();
        var stopCount = _storage.StopCount;
        var allStops = _storage.GetAllStops().ToList();

        _logger.LogInformation("📊 Start command diagnostics: HasData={HasData}, StopCount={StopCount}, GetAllStops.Count={Count}",
            hasData, stopCount, allStops.Count);

        if (!hasData || allStops.Count == 0)
        {
            _logger.LogWarning("⚠️ No stops available for user {UserId}", userId);

            if (IsAdmin(userId))
            {
                var adminKeyboard = new InlineKeyboardMarkup(
                    InlineKeyboardButton.WithCallbackData("🔄 Обновить расписание", "admin_refresh")
                );
                await SendWithRetryAsync(chatId,
                    "⚠️ Расписание не загружено.\n\nНажмите кнопку для обновления:",
                    replyMarkup: adminKeyboard);
                return;
            }

            await SendWithRetryAsync(chatId, BotMessages.NoSchedule);
            return;
        }

        _logger.LogInformation("✅ Showing {Count} stops to user {UserId}", allStops.Count, userId);

        var stopKeyboard = new InlineKeyboardMarkup(
            allStops.Select(stop => InlineKeyboardButton.WithCallbackData(
                stop.Name,
                $"{StopPrefix}{stop.Id}"))
            .ToArray()
        );

        await SendWithRetryAsync(chatId, BotMessages.Welcome, replyMarkup: stopKeyboard);
    }

    private async Task HandleUpdateCommand(long chatId, long userId)
    {
        if (!IsAdmin(userId))
        {
            await SendWithRetryAsync(chatId, BotMessages.NoRights);
            return;
        }

        _logger.LogInformation("Admin {UserId} requested manual update", userId);

        await SendWithRetryAsync(chatId, BotMessages.UpdateLoading);

        try
        {
            await _refreshService.ForceRefreshAsync();

            var stopCount = _storage.StopCount;
            var allStops = _storage.GetAllStops().ToList();

            _logger.LogInformation("After update: StopCount={StopCount}, GetAllStops count={Count}",
                stopCount, allStops.Count);

            await SendWithRetryAsync(chatId,
                $"✅ {BotMessages.UpdateSuccess}\n\n" +
                $"📊 Загружено остановок: {stopCount}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual update failed for user {UserId}", userId);
            await SendWithRetryAsync(chatId, $"❌ {BotMessages.UpdateFailed}");
        }
    }

    private async Task HandleHelpCommand(long chatId)
    {
        await SendWithRetryAsync(chatId, BotMessages.Help(), parseMode: ParseMode.Markdown);
    }

    private async Task HandleReportCommand(long chatId, long userId, string? reportText)
    {
        _logger.LogInformation("User {UserId} sent report: {ReportText}", userId, reportText ?? "empty");

        if (string.IsNullOrWhiteSpace(reportText))
        {
            await ShowReportInstructions(chatId);
            return;
        }

        try
        {
            var userState = _userStateService.GetOrCreate(userId);
            var fullName = string.IsNullOrEmpty(userState.FirstName)
                ? userId.ToString()
                : $"{userState.FirstName} {userState.LastName}".Trim();
            var username = string.IsNullOrEmpty(userState.Username)
                ? "без username"
                : $"@{userState.Username}";

            var adminMessage = $"📨 *Новое сообщение об ошибке*\n\n" +
                              $"👤 Пользователь: {EscapeMarkdown(fullName)}\n" +
                              $"🆔 ID: {userId}\n" +
                              $"📱 Username: {EscapeMarkdown(username)}\n\n" +
                              $"📝 *Сообщение:*\n{EscapeMarkdown(reportText)}";

            await _botClient.SendMessage(
                _adminUserId,
                adminMessage,
                parseMode: ParseMode.Markdown);

            await SendWithRetryAsync(chatId, "✅ Сообщение отправлено администратору. Спасибо!");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send report from user {UserId}", userId);
            await SendWithRetryAsync(chatId, "❌ Не удалось отправить сообщение. Попробуйте позже.");
        }
    }

    private async Task HandleStatusCommand(long chatId, long userId)
    {
        if (!IsAdmin(userId))
        {
            await SendWithRetryAsync(chatId, BotMessages.NoRights);
            return;
        }

        var status = _refreshService.GetStatus();
        var lastAttempt = _refreshService.GetLastAttemptInfo();
        var message = $"📊 *Статус бота*\n\n" +
                      $"• {status}\n" +
                      $"• {lastAttempt}\n" +
                      $"• Остановок в базе: {_storage.StopCount}\n" +
                      $"• Кэшированных рейсов: {_storage.CachedTripsCount}\n" +
                      $"• Активных пользователей: {_userStateService.GetActiveUsersCount()}\n" +
                      $"• Данные загружены: {(_storage.HasData() ? "✅" : "❌")}";

        await SendWithRetryAsync(chatId, message, parseMode: ParseMode.Markdown);
    }

    private async Task ShowReportInstructions(long chatId)
    {
        var adminLink = $"tg://user?id={_adminUserId}";
        var message = BotMessages.ReportInstructions(adminLink);
        await SendWithRetryAsync(chatId, message, parseMode: ParseMode.Markdown);
    }

    private async Task HandleTextMessage(long chatId, long userId, string text)
    {
        if (!_userStateService.HasStopSelected(userId))
        {
            await SendWithRetryAsync(chatId, BotMessages.ChooseStopFirst);
            return;
        }

        if (!_userStateService.TryGet(userId, out var state) ||
            string.IsNullOrEmpty(state.SelectedStopId))
        {
            await SendWithRetryAsync(chatId, BotMessages.ChooseStopFirst);
            return;
        }

        var stop = _storage.GetStop(state.SelectedStopId);
        if (stop is null)
        {
            await SendWithRetryAsync(chatId, BotMessages.StopNotFound);
            return;
        }

        _logger.LogInformation("User {UserId} requested {Command} for stop {StopId}",
            userId, text, stop.Id);

        switch (text)
        {
            case BotCommands.Nearest:
                await ShowNearest(chatId, stop);
                break;
            case BotCommands.Today:
                await ShowToday(chatId, stop);
                break;
            case BotCommands.Week:
                await ShowWeek(chatId, stop);
                break;
            case BotCommands.ChangeStop:
                await HandleStartCommand(chatId, userId);
                break;
            case BotCommands.Back:
                await HandleStartCommand(chatId, userId);
                break;
            default:
                await SendWithRetryAsync(chatId, BotMessages.UnknownCommand);
                break;
        }
    }

    private async Task ShowNearest(long chatId, Stop stop)
    {
        var now = DateTime.Now;
        var day = (int)now.DayOfWeek;
        var trips = _storage.GetTripsForDay(stop.Id, day);

        if (trips is null || trips.Count == 0)
        {
            await SendWithRetryAsync(chatId, BotMessages.NoTripsToday(stop.Name, (DayOfWeek)day));
            return;
        }

        var nowMinutes = now.Hour * TimeConstants.MinutesInHour + now.Minute;

        var sortedTrips = trips
            .Select(t => new { Trip = t, Minutes = TimeHelper.ToMinutes(t.Time) })
            .Where(x => x.Minutes.HasValue)
            .OrderBy(x => x.Minutes)
            .ToList();

        if (sortedTrips.Count == 0)
        {
            await SendWithRetryAsync(chatId, "⚠️ В расписании ошибка: все времена невалидны.");
            return;
        }

        var index = -1;
        for (int i = 0; i < sortedTrips.Count; i++)
        {
            if (sortedTrips[i].Minutes >= nowMinutes)
            {
                index = i;
                break;
            }
        }

        Trip? nearest = null;
        Trip? next = null;
        int? nearestMinutes = null;

        if (index >= 0)
        {
            nearest = sortedTrips[index].Trip;
            nearestMinutes = sortedTrips[index].Minutes;

            if (index + 1 < sortedTrips.Count)
            {
                next = sortedTrips[index + 1].Trip;
            }
        }

        if (nearest is null)
        {
            var nextDay = (DayOfWeek)((day + 1) % 7);
            var nextTrips = _storage.GetTripsForDay(stop.Id, (int)nextDay);

            if (nextTrips is null || nextTrips.Count == 0)
            {
                await SendWithRetryAsync(chatId, BotMessages.NoTripsTomorrow(nextDay));
                return;
            }

            var firstTomorrow = nextTrips
                .Select(t => new { Trip = t, Minutes = TimeHelper.ToMinutes(t.Time) })
                .FirstOrDefault(x => x.Minutes.HasValue);

            if (firstTomorrow is null)
            {
                await SendWithRetryAsync(chatId, BotMessages.NoTripsTomorrow(nextDay));
                return;
            }

            await SendWithRetryAsync(
                chatId,
                BotMessages.NearestTomorrow(nextDay, firstTomorrow.Trip.Time),
                parseMode: ParseMode.Markdown);
            return;
        }

        var response = $"🚌 *{EscapeMarkdown(stop.Name)}*\n\n";
        response += $"🟢 *Ближайший:* {nearest.Time}";
        if (!string.IsNullOrEmpty(nearest.Note))
            response += $" ({EscapeMarkdown(nearest.Note)})";

        var waitMinutes = nearestMinutes.Value - nowMinutes;
        if (waitMinutes < 0)
            waitMinutes += TimeConstants.DayMinutes;
        response += $"\n⏱ *через {waitMinutes} мин*\n\n";

        if (next is not null)
        {
            var nextMinutes = TimeHelper.ToMinutes(next.Time);
            response += $"🔵 *Следующий:* {next.Time}";
            if (!string.IsNullOrEmpty(next.Note))
                response += $" ({EscapeMarkdown(next.Note)})";
            if (nextMinutes.HasValue)
            {
                var nextWait = nextMinutes.Value - nowMinutes;
                if (nextWait < 0)
                    nextWait += TimeConstants.DayMinutes;
                response += $"\n⏱ *через {nextWait} мин*";
            }
        }
        else
        {
            response += "Следующих рейсов сегодня нет.";
        }

        await SendWithRetryAsync(chatId, response, parseMode: ParseMode.Markdown);
    }

    private async Task ShowToday(long chatId, Stop stop)
    {
        var now = DateTime.Now;
        var day = (int)now.DayOfWeek;
        var trips = _storage.GetTripsForDay(stop.Id, day);

        if (trips is null || trips.Count == 0)
        {
            await SendWithRetryAsync(chatId, BotMessages.NoTripsToday(stop.Name, (DayOfWeek)day));
            return;
        }

        var nowMinutes = now.Hour * TimeConstants.MinutesInHour + now.Minute;

        var sortedTrips = trips
            .Select(t => new { Trip = t, Minutes = TimeHelper.ToMinutes(t.Time) })
            .Where(x => x.Minutes.HasValue)
            .OrderBy(x => x.Minutes)
            .ToList();

        var response = $"📅 *Расписание на сегодня ({now:dddd})*\n";
        response += $"🚏 *{EscapeMarkdown(stop.Name)}*\n\n";

        foreach (var item in sortedTrips)
        {
            var mark = item.Minutes < nowMinutes ? "✅"
                : item.Minutes == nowMinutes ? "🔴"
                : "⬜";
            response += $"{mark} {item.Trip.Time}";
            if (!string.IsNullOrWhiteSpace(item.Trip.Note))
                response += $" ({EscapeMarkdown(item.Trip.Note)})";
            response += "\n";
        }

        await SendWithRetryAsync(chatId, response, parseMode: ParseMode.Markdown);
    }

    private async Task ShowWeek(long chatId, Stop stop)
    {
        var response = $"📅 *Расписание на неделю*\n";
        response += $"🚏 *{EscapeMarkdown(stop.Name)}*\n\n";

        for (int day = 0; day < 7; day++)
        {
            var dayName = (DayOfWeek)day;
            var trips = _storage.GetTripsForDay(stop.Id, day);
            response += $"*{dayName}*: ";

            if (trips is null || trips.Count == 0)
            {
                response += "нет рейсов\n";
            }
            else
            {
                var times = string.Join(", ",
                    trips
                        .Select(t => new { Trip = t, Minutes = TimeHelper.ToMinutes(t.Time) })
                        .Where(x => x.Minutes.HasValue)
                        .OrderBy(x => x.Minutes)
                        .Select(x => x.Trip.Time));
                response += $"{times}\n";
            }
        }

        await SendWithRetryAsync(chatId, response, parseMode: ParseMode.Markdown);
    }

    private static string EscapeMarkdown(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var chars = new[] { '_', '*', '[', ']', '(', ')', '~', '`', '>', '#', '+', '-', '=', '|', '{', '}', '.', '!' };
        foreach (var c in chars)
            text = text.Replace(c.ToString(), $"\\{c}");
        return text;
    }

    private async Task SendWithRetryAsync(
        long chatId,
        string text,
        ParseMode? parseMode = null,
        ReplyMarkup? replyMarkup = null,
        int maxRetries = 3,
        CancellationToken cancellationToken = default)
    {
        var actualParseMode = parseMode ?? ParseMode.Markdown;
        var actualText = text;

        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                await _botClient.SendMessage(
                    chatId,
                    actualText,
                    parseMode: actualParseMode,
                    replyMarkup: replyMarkup,
                    cancellationToken: cancellationToken);
                return;
            }
            catch (ApiRequestException ex) when (ex.ErrorCode == 429)
            {
                var retryAfter = ex.Parameters?.RetryAfter ?? 5;
                _logger.LogWarning("Rate limit hit. Retry after {RetryAfter}s (attempt {Attempt}/{MaxRetries})",
                    retryAfter, attempt + 1, maxRetries);
                await Task.Delay(retryAfter * 1000, cancellationToken);
            }
            catch (ApiRequestException ex) when (ex.ErrorCode == 403)
            {
                _logger.LogWarning("Cannot send message to user {ChatId}: bot was blocked", chatId);
                return;
            }
            catch (ApiRequestException ex) when (ex.ErrorCode == 400)
            {
                if (attempt == 0 && actualParseMode == ParseMode.Markdown)
                {
                    _logger.LogWarning("Markdown parse error, retrying without formatting: {Message}", ex.Message);
                    actualParseMode = ParseMode.Html;
                    actualText = System.Net.WebUtility.HtmlEncode(text);
                    continue;
                }
                _logger.LogWarning("Bad request when sending to {ChatId}: {Message}", chatId, ex.Message);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send message to {ChatId} (attempt {Attempt})",
                    chatId, attempt + 1);
                if (attempt == maxRetries - 1)
                    throw;
            }

            if (attempt < maxRetries - 1)
            {
                await Task.Delay(1000 * (attempt + 1), cancellationToken);
            }
        }
    }

    private async Task HandleTelegramApiException(ApiRequestException ex)
    {
        switch (ex.ErrorCode)
        {
            case 429:
                var retryAfter = ex.Parameters?.RetryAfter ?? 5;
                _logger.LogWarning("Rate limit exceeded: {Message}. Retry after {RetryAfter}s",
                    ex.Message, retryAfter);
                break;
            case 403:
                _logger.LogWarning("Bot was blocked or chat not found: {Message}", ex.Message);
                break;
            case 400:
                _logger.LogWarning("Bad request to Telegram API: {Message}", ex.Message);
                break;
            default:
                _logger.LogError(ex, "Telegram API error: {ErrorCode}", ex.ErrorCode);
                break;
        }
    }

    #endregion
}