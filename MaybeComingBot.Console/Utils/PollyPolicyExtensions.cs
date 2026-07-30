using MaybeComingBot.Services;
using Polly;
using Polly.Timeout;
using Serilog;
using System.Net;

namespace MaybeComingBot.Utils;

public static class PollyPolicyExtensions
{
    public static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
    {
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
                3,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    Log.ForContext<ScheduleLoader>()
                        .Warning("Retry {RetryCount} after {Delay}ms due to: {Error}",
                            retryCount,
                            timespan.TotalMilliseconds,
                            outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString());
                });
    }

    public static IAsyncPolicy<HttpResponseMessage> GetTimeoutPolicy()
    {
        return Policy
            .TimeoutAsync<HttpResponseMessage>(
                TimeSpan.FromSeconds(30),
                TimeoutStrategy.Pessimistic,
                (context, timespan, task) =>
                {
                    Log.ForContext<ScheduleLoader>()
                        .Warning("Request timed out after {Timeout} seconds", timespan.TotalSeconds);
                    return Task.CompletedTask;
                });
    }
}
