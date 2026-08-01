using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace MaybeComingBot.Services;

public class TelegramSender
{
    private readonly ITelegramBotClient _botClient;
    private readonly ILogger<TelegramSender> _logger;
    private readonly AsyncRetryPolicy _retryPolicy;

    public TelegramSender(
        ITelegramBotClient botClient,
        ILogger<TelegramSender> logger,
        int maxRetryCount = 3)
    {
        _botClient = botClient;
        _logger = logger;

        _retryPolicy = Policy
            .Handle<ApiRequestException>(ex =>
                ex.ErrorCode == 429 || // Too Many Requests
                ex.ErrorCode == 500 || // Internal Server Error
                ex.ErrorCode == 502 || // Bad Gateway
                ex.ErrorCode == 503 || // Service Unavailable
                ex.ErrorCode == 504)   // Gateway Timeout
            .WaitAndRetryAsync(
                maxRetryCount,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (ex, delay, retryCount, _) =>
                {
                    _logger.LogWarning(ex, "Telegram retry {Retry}/{Max}: {Delay}s",
                        retryCount, maxRetryCount, delay.TotalSeconds);
                });
    }

    public async Task SendAsync(
        long chatId,
        string text,
        ParseMode? parseMode = null,
        ReplyMarkup? replyMarkup = null,
        CancellationToken ct = default)
    {
        var originalParseMode = parseMode ?? ParseMode.Markdown;

        try
        {
            await SendWithRetryAsync(chatId, text, originalParseMode, replyMarkup, ct);
        }
        catch (OperationCanceledException)
        {
            // TaskCanceledException тоже перехватится здесь (так как он наследуется)
            _logger.LogDebug("Send cancelled for {ChatId}", chatId);
            // Не пробрасываем - нормальная остановка
        }
        catch (ApiRequestException ex) when (ex.ErrorCode == 403)
        {
            _logger.LogWarning(ex, "Bot blocked or user not found (403): {ChatId}", chatId);
            // Не пробрасываем - сообщение не может быть доставлено
        }
        catch (ApiRequestException ex) when (ex.ErrorCode == 400)
        {
            _logger.LogWarning(ex, "Bad request for {ChatId}: {Message}", chatId, ex.Message);
            // Не пробрасываем - ошибка формата сообщения
        }
        catch (ApiRequestException ex)
        {
            _logger.LogError(ex, "Telegram API error for {ChatId} (Code: {ErrorCode})", chatId, ex.ErrorCode);
            // Пробрасываем только если это критично
            throw;
        }
    }

    private async Task SendWithRetryAsync(
        long chatId,
        string text,
        ParseMode parseMode,
        ReplyMarkup? replyMarkup,
        CancellationToken ct)
    {
        try
        {
            await _retryPolicy.ExecuteAsync(
                async (cancellationToken) =>
                {
                    await _botClient.SendMessage(
                        chatId,
                        text,
                        parseMode: parseMode,
                        replyMarkup: replyMarkup,
                        cancellationToken: cancellationToken);

                    _logger.LogDebug("Message sent to {ChatId} with {ParseMode}", chatId, parseMode);
                },
                cancellationToken: ct);
        }
        catch (ApiRequestException ex) when (ex.ErrorCode == 400 && parseMode != ParseMode.None)
        {
            // Если ошибка парсинга - пробуем без форматирования
            _logger.LogWarning(ex, "Parse error for {ChatId}, retrying plain text", chatId);
            await SendWithRetryAsync(chatId, text, ParseMode.None, replyMarkup, ct);
        }
        catch (ApiRequestException ex) when (ex.ErrorCode == 400)
        {
            _logger.LogWarning(ex, "Bad request for {ChatId}: {Message}", chatId, ex.Message);
            // Не пробрасываем - ошибка формата сообщения
        }
        // Другие ошибки пробрасываем выше
    }
}