using MaybeComingBot.Abstractions;
using MaybeComingBot.Health;
using MaybeComingBot.Models;
using MaybeComingBot.Services;
using MaybeComingBot.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Telegram.Bot;

namespace MaybeComingBot.Extensions;

/// <summary>
/// Provides extension methods for registering bot services with the dependency injection container.
/// </summary>
/// <remarks>
/// This class centralizes all service registrations including configuration, HTTP clients,
/// Telegram client, business services, hosted services, and health checks.
/// </remarks>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all bot services with the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>
    /// The same <see cref="IServiceCollection"/> instance for method chaining.
    /// </returns>
    /// <remarks>
    /// This method registers the following service categories:
    /// <list type="number">
    /// <item><description>Configuration options (<see cref="BotSettings"/>)</description></item>
    /// <item><description>HTTP client with Polly retry policies for schedule loading</description></item>
    /// <item><description>Telegram bot client</description></item>
    /// <item><description>Business services (storage, state, formatting, dispatching)</description></item>
    /// <item><description>Background services (schedule refresh, user cleanup)</description></item>
    /// <item><description>Health checks for monitoring</description></item>
    /// </list>
    /// </remarks>
    public static IServiceCollection AddBotServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Registers BotSettings as IOptions with DataAnnotation validation.
        services.AddOptions<BotSettings>()
            .Bind(configuration.GetBotSection())
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Registers custom validator for additional cross-property validation.
        services.AddSingleton<IValidateOptions<BotSettings>, BotSettingsValidator>();

        // Registers HTTP client for schedule loading with Polly retry policies.
        services.AddHttpClient(nameof(ScheduleLoader), (provider, client) =>
        {
            var settings = provider.GetRequiredService<IOptions<BotSettings>>().Value;

            if (!string.IsNullOrEmpty(settings.ScheduleUrl))
            {
                client.BaseAddress = new Uri(settings.ScheduleUrl);
            }

            // Use the configured timeout for schedule HTTP requests.
            client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
        })
        .AddPolicyHandler((provider, _) =>
        {
            var settings = provider.GetRequiredService<IOptions<BotSettings>>();
            return PollyPolicyExtensions.GetRetryPolicy(settings);
        })
        .AddPolicyHandler((provider, _) =>
        {
            var settings = provider.GetRequiredService<IOptions<BotSettings>>();
            return PollyPolicyExtensions.GetTimeoutPolicy(settings);
        })
        .SetHandlerLifetime(TimeSpan.FromMinutes(5));

        // Registers Telegram bot client with token from settings.
        services.AddSingleton<ITelegramBotClient>(provider =>
        {
            var settings = provider.GetRequiredService<IOptions<BotSettings>>().Value;
            return new TelegramBotClient(settings.BotToken);
        });

        // Registers all business services as singletons.
        services.AddSingleton<IScheduleStorage, ScheduleStorage>();
        services.AddSingleton<RateLimiter>();
        services.AddSingleton<IUserStateService, UserStateService>();
        services.AddSingleton<ITelegramSender, TelegramSender>();
        services.AddSingleton<StopSelectionGuard>();
        services.AddSingleton<ScheduleFormatter>();
        services.AddSingleton<ScheduleLoader>();
        services.AddSingleton<CommandDispatcher>();
        services.AddSingleton<BotHandler>();

        // Registers the configured timezone as a singleton so that all services
        // computing "now" (ScheduleFormatter, ScheduleRefreshService) use the same,
        // user-facing timezone regardless of the server/container timezone.
        services.AddSingleton(provider =>
        {
            var settings = provider.GetRequiredService<IOptions<BotSettings>>().Value;

            // Honor the container-level TZ environment variable when set (e.g., in Docker).
            // On Linux, TZ is a valid IANA timezone identifier; this lets operators override
            // the app setting without redeploying.
            var rawTz = Environment.GetEnvironmentVariable("TZ");
            var timeZoneId = !string.IsNullOrWhiteSpace(rawTz) ? rawTz : settings.TimeZoneId;

            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            }
            catch (TimeZoneNotFoundException ex)
            {
                throw new InvalidOperationException(
                    $"Invalid timezone id '{timeZoneId}' (from TZ env var or BotSettings.TimeZoneId). " +
                    "Use a valid IANA id such as 'Europe/Minsk'.", ex);
            }
        });

        // Registers the schedule refresh service as a singleton so that both the
        // hosted background service and the IScheduleRefreshService consumers
        // (CommandDispatcher, BotHealthCheck) share the SAME instance. This is
        // critical: without a single instance, the /status command and health
        // check would observe a different, non-running instance.
        services.AddSingleton<ScheduleRefreshService>();
        services.AddSingleton<IScheduleRefreshService>(provider =>
            provider.GetRequiredService<ScheduleRefreshService>());

        // Registers background services that run continuously.
        services.AddHostedService<ScheduleRefreshService>();
        services.AddHostedService<UserStateCleanupService>();

        // Registers health check for monitoring bot status.
        services.AddHealthChecks().AddCheck<BotHealthCheck>("bot_health");

        // Hosts a minimal HTTP endpoint (GET /health) so container platforms
        // such as Railway can health-check the worker. Binds to $PORT.
        services.AddHostedService<HealthHttpServer>();

        return services;
    }
}

