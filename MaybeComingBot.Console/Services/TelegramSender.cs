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
            .Handle<ApiRequestException>(ex => ex.ErrorCode == 429)
            .WaitAndRetryAsync(
                maxRetryCount,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (ex, delay, retryCount, _) =>
                {
                    _logger.LogWarning("Telegram retry {Retry}/{Max}: {Delay}s",
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
        var actualParseMode = parseMode ?? ParseMode.Markdown;
        var actualText = text;

        await _retryPolicy.ExecuteAsync(async () =>
        {
            try
            {
                await _botClient.SendMessage(
                    chatId,
                    actualText,
                    parseMode: actualParseMode,
                    replyMarkup: replyMarkup,
                    cancellationToken: ct);
            }
            catch (ApiRequestException ex) when (ex.ErrorCode == 403)
            {
                _logger.LogWarning("Blocked or chat not found: {ChatId}", chatId);
            }
            catch (ApiRequestException ex) when (ex.ErrorCode == 400 && actualParseMode != ParseMode.None)
            {
                _logger.LogWarning("Parse error, retrying plain text");
                actualParseMode = ParseMode.None;
                await _botClient.SendMessage(chatId, actualText, replyMarkup: replyMarkup, cancellationToken: ct);
            }
            catch (ApiRequestException ex) when (ex.ErrorCode == 400)
            {
                _logger.LogWarning("Bad request: {Message}", ex.Message);
            }
        });
    }
}