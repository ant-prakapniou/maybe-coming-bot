using MaybeComingBot.Models;
using MaybeComingBot.Services;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Timeout;
using Serilog;
using System.Net;

namespace MaybeComingBot.Utils;

public static class PollyPolicyExtensions
{
    public static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy(IOptions<BotSettings> settings)
    {
        var maxRetries = settings.Value.MaxRetryCount;
        var baseDelay = settings.Value.RetryBaseDelaySeconds;

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
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(baseDelay, retryAttempt)),
                onRetry: (outcome, timespan, retryCount, _) =>
                {
                    if (retryCount == maxRetries)
                    {
                        Log.ForContext<ScheduleLoader>()
                            .Error("All {Max} retries failed", maxRetries);
                    }
                });
    }

    public static IAsyncPolicy<HttpResponseMessage> GetTimeoutPolicy()
    {
        return Policy.TimeoutAsync<HttpResponseMessage>(
            TimeSpan.FromSeconds(30),
            TimeoutStrategy.Optimistic);
    }
}