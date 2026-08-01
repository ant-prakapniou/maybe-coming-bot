using MaybeComingBot.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Timeout;
using System.Net;

namespace MaybeComingBot.Utils;

/// <summary>
/// Provides Polly policy configurations for HTTP request resilience.
/// </summary>
/// <remarks>
/// This class contains extension methods for creating retry, timeout, and combined policies
/// using the Polly library. All policies include jitter for distributed retry patterns
/// and support logging through the Polly context.
/// </remarks>
public static class PollyPolicyExtensions
{
    private static readonly Random _jitter = new();
    private const int DefaultTimeoutSeconds = 30;

    /// <summary>
    /// Creates a retry policy with exponential backoff and jitter for HTTP requests.
    /// </summary>
    /// <param name="settings">The bot settings containing retry configuration.</param>
    /// <returns>
    /// An asynchronous retry policy for <see cref="HttpResponseMessage"/>.
    /// </returns>
    /// <remarks>
    /// The policy handles the following conditions:
    /// <list type="bullet">
    /// <item><description>HTTP status codes: 408, 429, 500, 502, 503, 504</description></item>
    /// <item><description>HttpRequestException</description></item>
    /// <item><description>TimeoutRejectedException</description></item>
    /// <item><description>TaskCanceledException</description></item>
    /// </list>
    /// Retry delays follow exponential backoff: baseDelay × 2^(attempt-1)
    /// with jitter of up to 500ms to prevent thundering herd problems.
    /// </remarks>
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

    /// <summary>
    /// Creates a timeout policy for HTTP requests with pessimistic strategy.
    /// </summary>
    /// <param name="settings">Optional bot settings containing timeout configuration.</param>
    /// <returns>
    /// An asynchronous timeout policy for <see cref="HttpResponseMessage"/>.
    /// </returns>
    /// <remarks>
    /// Uses pessimistic timeout strategy which allows proper cancellation of underlying operations.
    /// The timeout duration is taken from <see cref="BotSettings.TimeoutSeconds"/> or defaults to 30 seconds.
    /// </remarks>
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

    /// <summary>
    /// Creates a combined policy that wraps retry and timeout policies.
    /// </summary>
    /// <param name="settings">The bot settings containing all policy configuration.</param>
    /// <returns>
    /// A combined asynchronous policy for <see cref="HttpResponseMessage"/>.
    /// </returns>
    /// <remarks>
    /// The combined policy applies retry logic first, with timeout enforcement
    /// at each retry attempt. This ensures individual attempts don't exceed
    /// the configured timeout limit.
    /// </remarks>
    public static IAsyncPolicy<HttpResponseMessage> GetCombinedPolicy(
        IOptions<BotSettings> settings)
    {
        return Policy.WrapAsync(
            GetRetryPolicy(settings),
            GetTimeoutPolicy(settings));
    }

    /// <summary>
    /// Retrieves an ILogger from the Polly context or returns NullLogger if not found.
    /// </summary>
    /// <param name="context">The Polly execution context.</param>
    /// <returns>
    /// An <see cref="ILogger"/> instance, never null.
    /// </returns>
    private static ILogger GetLoggerOrDefault(this Context context)
    {
        if (context.TryGetLogger(out var logger))
            return logger;

        return NullLogger.Instance;
    }
}

/// <summary>
/// Provides extension methods for managing ILogger in Polly Context.
/// </summary>
public static class PollyContextExtensions
{
    private const string LoggerKey = "Logger";

    /// <summary>
    /// Adds an ILogger to the Polly context for use in policy callbacks.
    /// </summary>
    /// <param name="context">The Polly execution context.</param>
    /// <param name="logger">The logger to store in the context.</param>
    /// <returns>
    /// The same context instance for method chaining.
    /// </returns>
    public static Context WithLogger(this Context context, ILogger logger)
    {
        context[LoggerKey] = logger;
        return context;
    }

    /// <summary>
    /// Attempts to retrieve an ILogger from the Polly context.
    /// </summary>
    /// <param name="context">The Polly execution context.</param>
    /// <param name="logger">When this method returns, contains the logger if found; otherwise, null.</param>
    /// <returns>
    /// <c>true</c> if a logger was found; otherwise, <c>false</c>.
    /// </returns>
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