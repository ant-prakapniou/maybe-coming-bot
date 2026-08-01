using MaybeComingBot.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Timeout;
using System.Net;

namespace MaybeComingBot.Utils;

public static class PollyPolicyExtensions
{
    public static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy(IOptions<BotSettings> settings)
    {
        var maxRetries = settings.Value.MaxRetryCount;
        var baseDelaySeconds = settings.Value.RetryBaseDelaySeconds;

        return Policy
            .Handle<HttpRequestException>()
            .Or<TaskCanceledException>()
            .OrResult<HttpResponseMessage>(r =>
                r.StatusCode is HttpStatusCode.RequestTimeout
                    or HttpStatusCode.TooManyRequests
                    or HttpStatusCode.InternalServerError
                    or HttpStatusCode.BadGateway
                    or HttpStatusCode.ServiceUnavailable
                    or HttpStatusCode.GatewayTimeout)
            .WaitAndRetryAsync(
                maxRetries,
                retryAttempt => TimeSpan.FromSeconds(
                    baseDelaySeconds * Math.Pow(2, retryAttempt - 1)), // 2, 4, 8, 16...
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    var logger = context.GetLoggerOrDefault();
                    var url = outcome.Result?.RequestMessage?.RequestUri?.ToString() ?? "unknown";
                    var statusCode = outcome.Result?.StatusCode.ToString() ?? "UNKNOWN";

                    // Логируем только если это не последняя попытка
                    if (retryCount < maxRetries)
                    {
                        logger?.LogWarning(
                            "Retry {Retry}/{Max} in {Delay:F1}s for {Url} (Status: {StatusCode})",
                            retryCount, maxRetries, timespan.TotalSeconds, url, statusCode);
                    }
                    else
                    {
                        // Последняя попытка - логируем как ошибку с деталями
                        logger?.LogError(
                            "All {Max} retries failed for {Url}. Last status: {StatusCode}",
                            maxRetries, url, statusCode);
                    }
                });
    }

    public static IAsyncPolicy<HttpResponseMessage> GetTimeoutPolicy()
    {
        // Pessimistic более надёжен для HTTP запросов
        return Policy.TimeoutAsync<HttpResponseMessage>(
            TimeSpan.FromSeconds(30),
            TimeoutStrategy.Pessimistic);
    }

    private static ILogger? GetLoggerOrDefault(this Context context)
    {
        if (context.TryGetLogger(out var logger))
            return logger;
        return null;
    }
}

public static class PollyContextExtensions
{
    private const string LoggerKey = "Logger";

    public static Context WithLogger(this Context context, ILogger logger)
    {
        context[LoggerKey] = logger;
        return context;
    }

    public static bool TryGetLogger(this Context context, out ILogger? logger)
    {
        if (context.TryGetValue(LoggerKey, out var value) && value is ILogger log)
        {
            logger = log;
            return true;
        }

        logger = null;
        return false;
    }
}