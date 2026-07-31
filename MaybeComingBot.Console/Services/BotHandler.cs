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
        try
        {
            if (update.Type == UpdateType.EditedMessage)
                return;

            var userId = GetUserId(update);
            var chatId = GetChatId(update);

            if (userId > 0)
                _userStateService.UpdateActivity(userId);

            if (update.Type == UpdateType.CallbackQuery)
            {
                await _dispatcher.HandleCallbackAsync(update.CallbackQuery!);
                return;
            }

            if (update.Type != UpdateType.Message)
                return;

            var message = update.Message;
            if (message is null || string.IsNullOrWhiteSpace(message.Text))
                return;

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
                await _botClient.SendMessage(chatId, BotMessages.TooManyRequests);
                return;
            }

            var (command, args) = ParseCommand(message.Text.Trim());
            await _dispatcher.DispatchCommandAsync(chatId, userId, command, args);
        }
        catch (ApiRequestException apiEx)
        {
            _logger.LogWarning(apiEx, "Telegram API error: {ErrorCode}", apiEx.ErrorCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling update");
            throw;
        }
    }

    private static long GetUserId(Update update)
        => update?.Message?.From?.Id ?? update?.CallbackQuery?.From?.Id ?? 0;

    private static long GetChatId(Update update)
        => update?.Message?.Chat?.Id ?? update?.CallbackQuery?.Message?.Chat?.Id ?? 0;

    private static (string Command, string? Args) ParseCommand(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (string.Empty, null);

        if (text.StartsWith('/'))
        {
            var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            return (parts[0], parts.Length > 1 ? parts[1].Trim() : null);
        }

        return (text, null);
    }
}