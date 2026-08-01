using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace MaybeComingBot.Services;

/// <summary>
/// Provides Telegram message sending capabilities with retry logic and error handling.
/// </summary>
/// <remarks>
/// This class wraps the Telegram Bot API client with:
/// <list type="bullet">
/// <item><description>Automatic retry with exponential backoff for transient errors</description></item>
/// <item><description>Text truncation to Telegram's 4096 character limit</description></item>
/// <item><description>Markdown parse error fallback to plain text</description></item>
/// <item><description>Graceful handling of common API errors (403, 400)</description></item>
/// <item><description>Support for editing messages and sending chat actions</description></item>
/// </list>
/// </remarks>
public class TelegramSender
{
    private readonly ITelegramBotClient _botClient;
    private readonly ILogger<TelegramSender> _logger;
    private readonly AsyncRetryPolicy _retryPolicy;
    private const int MaxMessageLength = 4096;
    private const int DefaultMaxRetryCount = 5;

    /// <summary>
    /// Initializes a new instance of the <see cref="TelegramSender"/> class.
    /// </summary>
    /// <param name="botClient">The Telegram bot client.</param>
    /// <param name="logger">The logger instance for this class.</param>
    /// <param name="maxRetryCount">Maximum number of retry attempts. Default is 5.</param>
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

    /// <summary>
    /// Sends a message to a Telegram chat.
    /// </summary>
    /// <param name="chatId">The Telegram chat ID.</param>
    /// <param name="text">The message text to send.</param>
    /// <param name="parseMode">Optional parse mode (Markdown, HTML, etc.).</param>
    /// <param name="replyMarkup">Optional reply markup (keyboard or inline buttons).</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// <para>
    /// The method handles:
    /// <list type="bullet">
    /// <item><description>Text truncation to 4096 characters</description></item>
    /// <item><description>Retry on transient errors (429, 5xx)</description></item>
    /// <item><description>Silent handling of 403 (bot blocked)</description></item>
    /// <item><description>Silent handling of 400 (bad request)</description></item>
    /// <item><description>Markdown parse errors (falls back to plain text)</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// Most errors are handled gracefully and do not throw exceptions,
    /// with the exception of unexpected API errors and timeouts.
    /// </para>
    /// </remarks>
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

        var truncatedText = TruncateText(text);
        var originalParseMode = parseMode ?? ParseMode.Markdown;

        try
        {
            await SendWithRetryAsync(chatId, truncatedText, originalParseMode, replyMarkup, ct);
        }
        catch (OperationCanceledException ex) when (ct.IsCancellationRequested)
        {
            _logger.LogDebug("Send cancelled by user for {ChatId}", chatId);
            // Don't rethrow - normal shutdown
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Send timed out for {ChatId}", chatId);
            throw; // Timeout - rethrow as problem
        }
        catch (ApiRequestException ex) when (ex.ErrorCode == 403)
        {
            _logger.LogWarning(ex, "Bot blocked or user not found (403): {ChatId}", chatId);
            // Don't rethrow - message cannot be delivered
        }
        catch (ApiRequestException ex) when (ex.ErrorCode == 400)
        {
            _logger.LogWarning(ex, "Bad request for {ChatId}: {Message}", chatId, ex.Message);
            // Don't rethrow - message formatting error
        }
        catch (ApiRequestException ex)
        {
            _logger.LogError(ex, "Telegram API error for {ChatId} (Code: {ErrorCode})", chatId, ex.ErrorCode);
            throw;
        }
    }

    /// <summary>
    /// Internal method that sends a message with retry logic.
    /// </summary>
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
            // If parse error, retry without formatting
            _logger.LogWarning(ex, "Parse error for {ChatId}, retrying plain text", chatId);
            await SendWithRetryAsync(chatId, text, ParseMode.None, replyMarkup, ct, true);
        }
        catch (ApiRequestException ex) when (ex.ErrorCode == 400)
        {
            _logger.LogWarning(ex, "Bad request for {ChatId}: {Message}", chatId, ex.Message);
            // Don't rethrow - message formatting error
        }
        // Other errors are rethrown
    }

    /// <summary>
    /// Answers a callback query to remove the loading indicator.
    /// </summary>
    /// <param name="callbackQueryId">The callback query ID to answer.</param>
    /// <param name="text">Optional text to show in a toast notification.</param>
    /// <param name="showAlert">Whether to show the text as an alert.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="ArgumentException">Thrown when callbackQueryId is null or empty.</exception>
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

    /// <summary>
    /// Edits an existing message.
    /// </summary>
    /// <param name="chatId">The Telegram chat ID.</param>
    /// <param name="messageId">The ID of the message to edit.</param>
    /// <param name="text">The new message text.</param>
    /// <param name="parseMode">Optional parse mode (Markdown, HTML, etc.).</param>
    /// <param name="replyMarkup">Optional inline keyboard.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// If a Markdown parse error occurs, the method automatically retries
    /// with plain text formatting.
    /// </remarks>
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
            // If Markdown parse error, retry without formatting
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

    /// <summary>
    /// Sends a chat action (typing, uploading, etc.) to indicate bot activity.
    /// </summary>
    /// <param name="chatId">The Telegram chat ID.</param>
    /// <param name="chatAction">The chat action to send.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// Errors are logged but not thrown to prevent disrupting the main flow.
    /// </remarks>
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

    /// <summary>
    /// Truncates text to Telegram's maximum message length.
    /// </summary>
    /// <param name="text">The text to truncate.</param>
    /// <returns>Truncated text with "..." appended if truncated.</returns>
    private static string TruncateText(string text)
    {
        if (text.Length <= MaxMessageLength)
            return text;

        return text[..(MaxMessageLength - 3)] + "...";
    }
}