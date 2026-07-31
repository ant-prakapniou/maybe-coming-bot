using Polly;
using Polly.Retry;
using Serilog;
using Telegram.Bot.Exceptions;

namespace MaybeComingBot.Utils;

public static class TelegramRetryPolicy
{
    public static AsyncRetryPolicy Create(int maxRetries)
    {
        return Policy
            .Handle<ApiRequestException>(ex => ex.ErrorCode == 429)
            .WaitAndRetryAsync(
                maxRetries,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (ex, delay, retryCount, _) =>
                {
                    Log.ForContext("SourceContext", "TelegramRetry")
                        .Warning("Retry {Retry}/{Max}: {Delay}s",
                            retryCount, maxRetries, delay.TotalSeconds);
                });
    }
}