using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace MaybeComingBot.Services;

public class TelegramSender
{
    private readonly ITelegramBotClient _botClient;
    private readonly ILogger<TelegramSender> _logger;
    private readonly AsyncRetryPolicy _retryPolicy;
    private const int MaxMessageLength = 4096;
    private const int DefaultMaxRetryCount = 5;

    public TelegramSender(
        ITelegramBotClient botClient,
        ILogger<TelegramSender> logger,
        int maxRetryCount = DefaultMaxRetryCount)
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
                    _logger.LogWarning(ex, "Telegram retry {Retry}/{Max}: {Delay:F1}s",
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
        if (string.IsNullOrWhiteSpace(text))
        {
            _logger.LogWarning("Empty text for {ChatId}, skipping", chatId);
            return;
        }

        // Обрезаем текст, если он слишком длинный
        var truncatedText = TruncateText(text);
        var originalParseMode = parseMode ?? ParseMode.Markdown;

        try
        {
            await SendWithRetryAsync(chatId, truncatedText, originalParseMode, replyMarkup, ct);
        }
        catch (OperationCanceledException ex) when (ct.IsCancellationRequested)
        {
            _logger.LogDebug("Send cancelled by user for {ChatId}", chatId);
            // Не пробрасываем - нормальная остановка
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Send timed out for {ChatId}", chatId);
            throw; // Таймаут - проблема, пробрасываем
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
            throw;
        }
    }

    private async Task SendWithRetryAsync(
        long chatId,
        string text,
        ParseMode parseMode,
        ReplyMarkup? replyMarkup,
        CancellationToken ct,
        bool isRetryWithPlainText = false)
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

                    _logger.LogTrace("Message sent to {ChatId} with {ParseMode}", chatId, parseMode);
                },
                cancellationToken: ct);
        }
        catch (ApiRequestException ex) when (ex.ErrorCode == 400 &&
                                             parseMode != ParseMode.None &&
                                             !isRetryWithPlainText)
        {
            // Если ошибка парсинга - пробуем без форматирования
            _logger.LogWarning(ex, "Parse error for {ChatId}, retrying plain text", chatId);
            await SendWithRetryAsync(chatId, text, ParseMode.None, replyMarkup, ct, true);
        }
        catch (ApiRequestException ex) when (ex.ErrorCode == 400)
        {
            _logger.LogWarning(ex, "Bad request for {ChatId}: {Message}", chatId, ex.Message);
            // Не пробрасываем - ошибка формата сообщения
        }
        // Другие ошибки пробрасываем выше
    }

    public async Task AnswerCallbackAsync(
        string callbackQueryId,
        string? text = null,
        bool showAlert = false,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(callbackQueryId))
            throw new ArgumentException("Callback ID cannot be empty", nameof(callbackQueryId));

        try
        {
            await _botClient.AnswerCallbackQuery(
                callbackQueryId,
                text,
                showAlert,
                cancellationToken: ct);

            _logger.LogTrace("Callback answered: {CallbackId}", callbackQueryId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to answer callback {CallbackId}", callbackQueryId);
            throw;
        }
    }

    public async Task EditMessageAsync(
        long chatId,
        int messageId,
        string text,
        ParseMode? parseMode = null,
        InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            _logger.LogWarning("Empty text for editing message {MessageId}", messageId);
            return;
        }

        var truncatedText = TruncateText(text);

        try
        {
            await _botClient.EditMessageText(
                chatId,
                messageId,
                truncatedText,
                parseMode: parseMode ?? ParseMode.Markdown,
                replyMarkup: replyMarkup,
                cancellationToken: ct);

            _logger.LogTrace("Message {MessageId} edited for {ChatId}", messageId, chatId);
        }
        catch (ApiRequestException ex) when (ex.ErrorCode == 400)
        {
            // Если ошибка парсинга Markdown, пробуем без форматирования
            if (parseMode != ParseMode.None)
            {
                _logger.LogWarning(ex, "Parse error editing message {MessageId}, retrying plain text", messageId);
                await _botClient.EditMessageText(
                    chatId,
                    messageId,
                    truncatedText,
                    parseMode: ParseMode.None,
                    replyMarkup: replyMarkup,
                    cancellationToken: ct);
                return;
            }

            _logger.LogWarning(ex, "Bad request editing message {MessageId}", messageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to edit message {MessageId}", messageId);
            throw;
        }
    }

    public async Task SendChatActionAsync(
        long chatId,
        ChatAction chatAction,
        CancellationToken ct = default)
    {
        try
        {
            await _botClient.SendChatAction(chatId, chatAction, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send chat action for {ChatId}", chatId);
        }
    }

    private static string TruncateText(string text)
    {
        if (text.Length <= MaxMessageLength)
            return text;

        // Обрезаем и добавляем индикатор обрезки
        var truncated = text[..(MaxMessageLength - 3)] + "...";
        return truncated;
    }
}