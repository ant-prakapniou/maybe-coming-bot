using MaybeComingBot.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Timeout;
using System.Net;

namespace MaybeComingBot.Utils;

public static class PollyPolicyExtensions
{
    private static readonly Random _jitter = new();
    private const int DefaultTimeoutSeconds = 30;

    public static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy(IOptions<BotSettings> settings)
    {
        var maxRetries = settings.Value.MaxRetryCount;
        var baseDelaySeconds = settings.Value.RetryBaseDelaySeconds;

        return Policy
            .HandleResult<HttpResponseMessage>(r =>
                r.StatusCode is HttpStatusCode.RequestTimeout
                    or HttpStatusCode.TooManyRequests
                    or HttpStatusCode.InternalServerError
                    or HttpStatusCode.BadGateway
                    or HttpStatusCode.ServiceUnavailable
                    or HttpStatusCode.GatewayTimeout)
            .Or<HttpRequestException>()
            .Or<TimeoutRejectedException>()
            .Or<TaskCanceledException>()
            .WaitAndRetryAsync(
                maxRetries,
                retryAttempt =>
                {
                    var baseDelay = TimeSpan.FromSeconds(
                        baseDelaySeconds * Math.Pow(2, retryAttempt - 1));
                    var jitterMs = _jitter.Next(0, 500);
                    return baseDelay + TimeSpan.FromMilliseconds(jitterMs);
                },
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    var logger = context.GetLoggerOrDefault();

                    string url = "unknown";
                    string statusCode = "UNKNOWN";

                    if (outcome.Result is HttpResponseMessage response)
                    {
                        url = response.RequestMessage?.RequestUri?.ToString() ?? "unknown";
                        statusCode = response.StatusCode.ToString();
                    }
                    else if (outcome.Exception is not null)
                    {
                        statusCode = outcome.Exception.GetType().Name;
                    }

                    if (retryCount < maxRetries)
                    {
                        logger?.LogDebug(
                            "Retry {Retry}/{Max} in {Delay:F1}s for {Url} (Status: {StatusCode})",
                            retryCount, maxRetries, timespan.TotalSeconds, url, statusCode);
                    }
                    else
                    {
                        logger?.LogError(
                            "All {Max} retries failed for {Url}. Last status: {StatusCode}",
                            maxRetries, url, statusCode);
                    }
                });
    }

    public static IAsyncPolicy<HttpResponseMessage> GetTimeoutPolicy(
        IOptions<BotSettings>? settings = null)
    {
        var timeoutSeconds = settings?.Value?.TimeoutSeconds > 0
            ? settings.Value.TimeoutSeconds
            : DefaultTimeoutSeconds;

        return Policy.TimeoutAsync<HttpResponseMessage>(
            TimeSpan.FromSeconds(timeoutSeconds),
            TimeoutStrategy.Pessimistic);
    }

    // ⬇️ Убрали GetCircuitBreakerPolicy()

    public static IAsyncPolicy<HttpResponseMessage> GetCombinedPolicy(
        IOptions<BotSettings> settings)
    {
        return Policy.WrapAsync(
            GetRetryPolicy(settings),
            GetTimeoutPolicy(settings));
    }

    private static ILogger GetLoggerOrDefault(this Context context)
    {
        if (context.TryGetLogger(out var logger))
            return logger;

        return NullLogger.Instance;
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