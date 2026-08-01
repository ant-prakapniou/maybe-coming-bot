using MaybeComingBot.Abstractions;
using MaybeComingBot.Constants;
using MaybeComingBot.Utils;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace MaybeComingBot.Services;

public class BotHandler(
    ITelegramBotClient botClient,
    IUserStateService userStateService,
    CommandDispatcher dispatcher,
    RateLimiter rateLimiter,
    ILogger<BotHandler> logger)
{
    private readonly ITelegramBotClient _botClient = botClient;
    private readonly IUserStateService _userStateService = userStateService;
    private readonly CommandDispatcher _dispatcher = dispatcher;
    private readonly RateLimiter _rateLimiter = rateLimiter;
    private readonly ILogger<BotHandler> _logger = logger;

    public async Task HandleUpdateAsync(Update update)
    {
        // Вычисляем один раз
        var userId = GetUserId(update);
        var chatId = GetChatId(update);

        try
        {
            // Игнорируем edited messages
            if (update.Type == UpdateType.EditedMessage)
                return;

            // Обновляем активность пользователя (если это не бот)
            if (userId > 0)
                _userStateService.UpdateActivity(userId);

            // Обработка CallbackQuery
            if (update.Type == UpdateType.CallbackQuery)
            {
                if (update.CallbackQuery is not null)
                    await _dispatcher.HandleCallbackAsync(update.CallbackQuery);
                return;
            }

            // Обрабатываем только сообщения
            if (update.Type != UpdateType.Message)
                return;

            var message = update.Message;
            if (message is null || string.IsNullOrWhiteSpace(message.Text))
                return;

            // Игнорируем сообщения от ботов
            if (message.From?.IsBot == true)
                return;

            // Сохраняем информацию о пользователе
            if (message.From is not null)
            {
                _userStateService.SetUserInfo(
                    userId,
                    message.From.FirstName ?? string.Empty,
                    message.From.LastName,
                    message.From.Username);
            }

            // Rate limiting
            if (!await _rateLimiter.IsAllowedAsync(userId))
            {
                await _botClient.SendMessage(chatId, BotMessages.TooManyRequests);
                return;
            }

            // Парсим и отправляем команду
            var (command, args) = ParseCommand(message.Text.Trim());
            await _dispatcher.DispatchCommandAsync(chatId, userId, command, args);
        }
        catch (OperationCanceledException ex) when (ex.CancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Update handling cancelled (OperationCanceled)");
        }
        catch (TaskCanceledException ex) when (ex.CancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Update handling cancelled (TaskCanceled)");
        }
        catch (ApiRequestException apiEx)
        {
            _logger.LogWarning(apiEx, "Telegram API error: {ErrorCode}", apiEx.ErrorCode);
            // Не пробрасываем — бот продолжает работать
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error handling update (UserId: {UserId}, ChatId: {ChatId})",
                userId, chatId);
            // Не пробрасываем — бот продолжает работать
        }
    }

    private static long GetUserId(Update update)
    {
        if (update?.Message?.From != null)
            return update.Message.From.Id;

        if (update?.CallbackQuery?.From != null)
            return update.CallbackQuery.From.Id;

        return 0;
    }

    private static long GetChatId(Update update)
    {
        if (update?.Message?.Chat != null)
            return update.Message.Chat.Id;

        if (update?.CallbackQuery?.Message?.Chat != null)
            return update.CallbackQuery.Message.Chat.Id;

        // Для инлайн-режима (без чата) возвращаем 0
        return 0;
    }

    private static (string Command, string? Args) ParseCommand(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (string.Empty, null);

        if (text.StartsWith('/'))
        {
            var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var command = parts[0];

            // Отсекаем @BotName, если есть (например, /command@MyBot -> /command)
            var atIndex = command.IndexOf('@');
            if (atIndex > 0)
                command = command[..atIndex];

            return (command, parts.Length > 1 ? parts[1].Trim() : null);
        }

        return (text, null);
    }
}