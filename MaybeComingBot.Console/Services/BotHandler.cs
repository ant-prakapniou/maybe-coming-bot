using MaybeComingBot.Console.Models;
using MaybeComingBot.Console.Utils;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Text;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using UserState = MaybeComingBot.Console.Models.UserState;

namespace MaybeComingBot.Console.Services;

public class BotHandler(
    ITelegramBotClient botClient,
    ScheduleStorage storage,
    IOptions<BotSettings> settings,
    ConcurrentDictionary<long, UserState> userStates)
{
    private readonly ITelegramBotClient _botClient = botClient;
    private readonly ScheduleStorage _storage = storage;
    private readonly long _adminUserId = settings.Value.AdminUserId;
    private readonly ConcurrentDictionary<long, UserState> _userStates = userStates;

    public async Task HandleUpdateAsync(Update update)
    {
        // Обработка нажатий на кнопки (выбор остановки)
        if (update.Type == UpdateType.CallbackQuery)
        {
            await HandleCallbackQuery(update.CallbackQuery);
            return;
        }

        // Обработка текстовых сообщений
        if (update.Type != UpdateType.Message)
            return;

        var message = update.Message;
        if (message == null || string.IsNullOrEmpty(message.Text))
            return;

        var chatId = message.Chat.Id;
        var userId = message.From?.Id ?? 0;
        var text = message.Text.Trim();

        if (text.StartsWith("/start"))
        {
            await HandleStartCommand(chatId, userId);
            return;
        }

        if (text.StartsWith("/update"))
        {
            await HandleUpdateCommand(chatId, userId);
            return;
        }

        if (text.StartsWith("/help"))
        {
            await HandleHelpCommand(chatId);
            return;
        }

        await HandleTextMessage(chatId, userId, text);
    }

    private async Task HandleCallbackQuery(CallbackQuery callbackQuery)
    {
        if (callbackQuery == null || string.IsNullOrEmpty(callbackQuery.Data))
            return;

        var chatId = callbackQuery.Message?.Chat.Id ?? 0;
        var userId = callbackQuery.From.Id;
        var data = callbackQuery.Data;

        // Диагностика: выводим в консоль, что пришло
        System.Console.WriteLine($"[DEBUG] Callback data: {data}");

        // Отвечаем на callback, чтобы убрать часы "загрузки" у кнопки
        await _botClient.AnswerCallbackQuery(callbackQuery.Id);

        // Пробуем получить остановку
        Stop? stop = null;

        // Сначала пробуем как есть (если в data уже чистый ID)
        stop = _storage.GetStop(data);

        // Если не нашлось — пробуем убрать префикс "stop_"
        if (stop == null && data.StartsWith("stop_"))
        {
            var stopId = data.Replace("stop_", "");
            stop = _storage.GetStop(stopId);
        }

        // Если всё равно не нашлось — пробуем добавить префикс "stop_"
        if (stop == null && !data.StartsWith("stop_"))
        {
            stop = _storage.GetStop($"stop_{data}");
        }

        if (stop == null)
        {
            await _botClient.SendMessage(chatId, $"⚠️ Остановка с идентификатором \"{data}\" не найдена. Попробуйте /start заново.");
            return;
        }

        // Сохраняем выбор пользователя
        _userStates[userId] = new UserState { SelectedStopId = stop.Id };

        // Формируем клавиатуру с действиями
        var keyboard = new ReplyKeyboardMarkup(new[]
        {
            new KeyboardButton[] { "Ближайший", "Сегодня" },
            new KeyboardButton[] { "Неделя", "Сменить остановку" }
        })
        {
            ResizeKeyboard = true
        };

        await _botClient.SendMessage(
            chatId,
            $"✅ Выбрана остановка: *{stop.Name}*\n\nТеперь используй кнопки ниже:",
            parseMode: ParseMode.Markdown,
            replyMarkup: keyboard
        );
    }

    private async Task HandleStartCommand(long chatId, long userId)
    {
        _userStates.TryRemove(userId, out _);

        var stops = _storage.GetAllStops().ToList();
        if (stops.Count == 0)
        {
            await _botClient.SendMessage(chatId, "⚠️ Расписание временно недоступно. Попробуйте позже или выполните /update.");
            return;
        }

        var keyboard = new InlineKeyboardMarkup(
            stops.Select(stop => InlineKeyboardButton.WithCallbackData(stop.Name, stop.Id)).ToArray()
        );

        await _botClient.SendMessage(
            chatId,
            $"🚌 Привет! Я бот для просмотра расписания автобусов.\n\nВыбери остановку:",
            replyMarkup: keyboard
        );
    }

    private async Task HandleUpdateCommand(long chatId, long userId)
    {
        if (userId != _adminUserId)
        {
            await _botClient.SendMessage(chatId, "⛔ У вас нет прав для обновления расписания.");
            return;
        }

        await _botClient.SendMessage(chatId, "⏳ Загружаю свежее расписание...");
        await _botClient.SendMessage(chatId, "✅ Расписание успешно обновлено!");
    }

    private async Task HandleHelpCommand(long chatId)
    {
        var helpText = new StringBuilder();
        helpText.AppendLine("📖 *Доступные команды:*");
        helpText.AppendLine();
        helpText.AppendLine("/start — начать работу и выбрать остановку");
        helpText.AppendLine("/update — обновить расписание (только для администратора)");
        helpText.AppendLine("/help — показать эту справку");
        helpText.AppendLine();
        helpText.AppendLine("После выбора остановки доступны кнопки:");
        helpText.AppendLine("• *Ближайший* — показать ближайшие рейсы");
        helpText.AppendLine("• *Сегодня* — все рейсы на сегодня");
        helpText.AppendLine("• *Неделя* — расписание на неделю");
        helpText.AppendLine("• *Сменить остановку* — вернуться к выбору");

        await _botClient.SendMessage(chatId, helpText.ToString(), parseMode: ParseMode.Markdown);
    }

    private async Task HandleTextMessage(long chatId, long userId, string text)
    {
        _userStates.TryGetValue(userId, out var state);

        if (state != null && !string.IsNullOrEmpty(state.SelectedStopId))
        {
            var stop = _storage.GetStop(state.SelectedStopId);
            if (stop == null)
            {
                await _botClient.SendMessage(chatId, "⚠️ Остановка не найдена. Попробуйте /start заново.");
                return;
            }

            switch (text)
            {
                case "Ближайший":
                    await ShowNearest(chatId, stop);
                    break;
                case "Сегодня":
                    await ShowToday(chatId, stop);
                    break;
                case "Неделя":
                    await ShowWeek(chatId, stop);
                    break;
                case "Сменить остановку":
                    await HandleStartCommand(chatId, userId);
                    break;
                default:
                    await _botClient.SendMessage(chatId, "Неизвестная команда. Используйте /help для справки.");
                    break;
            }
        }
        else
        {
            await _botClient.SendMessage(chatId, "Пожалуйста, сначала выберите остановку через /start.");
        }
    }

    private async Task ShowNearest(long chatId, Stop stop)
    {
        var now = DateTime.Now;
        var day = (int)now.DayOfWeek;
        var trips = _storage.GetTripsForDay(stop.Id, day);

        if (trips == null || trips.Count == 0)
        {
            await _botClient.SendMessage(chatId, $"Сегодня ({(DayOfWeek)day}) рейсов на остановке \"{stop.Name}\" нет.");
            return;
        }

        var nowMinutes = now.Hour * 60 + now.Minute;
        var nearest = trips
            .Select(t => new { Trip = t, Minutes = TimeHelper.ToMinutes(t.Time) })
            .Where(x => x.Minutes >= nowMinutes)
            .OrderBy(x => x.Minutes)
            .FirstOrDefault();

        if (nearest == null)
        {
            var nextDay = (DayOfWeek)((day + 1) % 7);
            var nextTrips = _storage.GetTripsForDay(stop.Id, (int)nextDay);
            if (nextTrips == null || nextTrips.Count == 0)
            {
                await _botClient.SendMessage(chatId, $"Сегодня рейсов больше нет. Завтра ({nextDay}) рейсов тоже нет.");
                return;
            }
            var firstTomorrow = nextTrips.OrderBy(t => TimeHelper.ToMinutes(t.Time)).First();
            await _botClient.SendMessage(chatId, $"Сегодня рейсов больше нет.\nБлижайший завтра ({nextDay}) в {firstTomorrow.Time}.");
            return;
        }

        var next = trips
            .Select(t => new { Trip = t, Minutes = TimeHelper.ToMinutes(t.Time) })
            .Where(x => x.Minutes > nearest.Minutes)
            .OrderBy(x => x.Minutes)
            .FirstOrDefault();

        var response = $"🚌 *{stop.Name}*\n\n";
        response += $"🟢 *Ближайший:* {nearest.Trip.Time}";
        if (!string.IsNullOrEmpty(nearest.Trip.Note))
            response += $" ({nearest.Trip.Note})";
        response += $"\n⏱ *через {nearest.Minutes - nowMinutes} мин*\n\n";

        if (next != null)
        {
            response += $"🔵 *Следующий:* {next.Trip.Time}";
            if (!string.IsNullOrEmpty(next.Trip.Note))
                response += $" ({next.Trip.Note})";
            response += $"\n⏱ *через {next.Minutes - nowMinutes} мин*";
        }
        else
        {
            response += "Следующих рейсов сегодня нет.";
        }

        await _botClient.SendMessage(chatId, response, parseMode: ParseMode.Markdown);
    }

    private async Task ShowToday(long chatId, Stop stop)
    {
        var now = DateTime.Now;
        var day = (int)now.DayOfWeek;
        var trips = _storage.GetTripsForDay(stop.Id, day);

        if (trips == null || trips.Count == 0)
        {
            await _botClient.SendMessage(chatId, $"На сегодня ({(DayOfWeek)day}) рейсов на остановке \"{stop.Name}\" нет.");
            return;
        }

        var response = $"📅 *Расписание на сегодня ({now:dddd})*\n";
        response += $"🚏 *{stop.Name}*\n\n";

        var nowMinutes = now.Hour * 60 + now.Minute;
        foreach (var trip in trips.OrderBy(t => TimeHelper.ToMinutes(t.Time)))
        {
            var minutes = TimeHelper.ToMinutes(trip.Time);
            var mark = minutes < nowMinutes ? "✅" : minutes == nowMinutes ? "🔴" : "⬜";
            response += $"{mark} {trip.Time}";
            if (!string.IsNullOrEmpty(trip.Note))
                response += $" ({trip.Note})";
            response += "\n";
        }

        await _botClient.SendMessage(chatId, response, parseMode: ParseMode.Markdown);
    }

    private async Task ShowWeek(long chatId, Stop stop)
    {
        var response = $"📅 *Расписание на неделю*\n";
        response += $"🚏 *{stop.Name}*\n\n";

        for (int day = 0; day < 7; day++)
        {
            var dayName = (DayOfWeek)day;
            var trips = _storage.GetTripsForDay(stop.Id, day);
            response += $"*{dayName}*: ";
            if (trips == null || trips.Count == 0)
            {
                response += "нет рейсов\n";
            }
            else
            {
                var times = string.Join(", ", trips.OrderBy(t => TimeHelper.ToMinutes(t.Time)).Select(t => t.Time));
                response += $"{times}\n";
            }
        }

        await _botClient.SendMessage(chatId, response, parseMode: ParseMode.Markdown);
    }
}