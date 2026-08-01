using MaybeComingBot.Abstractions;
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

        /// <summary>
        /// Registers BotSettings as IOptions with DataAnnotation validation.
        /// </summary>
        services.AddOptions<BotSettings>()
            .Bind(configuration.GetBotSection())
            .ValidateDataAnnotations()
            .ValidateOnStart();

        /// <summary>
        /// Registers custom validator for additional cross-property validation.
        /// </summary>
        services.AddSingleton<IValidateOptions<BotSettings>, BotSettingsValidator>();

        /// <summary>
        /// Registers HTTP client for schedule loading with Polly retry policies.
        /// </summary>
        services.AddHttpClient(nameof(ScheduleLoader), client =>
        {
            var settings = configuration.GetBotSettings();
            if (settings is not null && !string.IsNullOrEmpty(settings.ScheduleUrl))
            {
                client.BaseAddress = new Uri(settings.ScheduleUrl);
            }
            client.Timeout = TimeSpan.FromSeconds(30);
        })
        .AddPolicyHandler((provider, _) =>
        {
            var settings = provider.GetRequiredService<IOptions<BotSettings>>();
            return PollyPolicyExtensions.GetRetryPolicy(settings);
        })
        .AddPolicyHandler(PollyPolicyExtensions.GetTimeoutPolicy())
        .SetHandlerLifetime(TimeSpan.FromMinutes(5));

        /// <summary>
        /// Registers Telegram bot client with token from settings.
        /// </summary>
        services.AddSingleton<ITelegramBotClient>(provider =>
        {
            var settings = provider.GetRequiredService<IOptions<BotSettings>>().Value;
            return new TelegramBotClient(settings.BotToken);
        });

        /// <summary>
        /// Registers all business services as singletons.
        /// </summary>
        services.AddSingleton<IScheduleStorage, ScheduleStorage>();
        services.AddSingleton<RateLimiter>();
        services.AddSingleton<IUserStateService, UserStateService>();
        services.AddSingleton<TelegramSender>();
        services.AddSingleton<StopSelectionGuard>();
        services.AddSingleton<ScheduleFormatter>();
        services.AddSingleton<ScheduleLoader>();
        services.AddSingleton<IScheduleRefreshService, ScheduleRefreshService>();
        services.AddSingleton<CommandDispatcher>();
        services.AddSingleton<BotHandler>();

        /// <summary>
        /// Registers background services that run continuously.
        /// </summary>
        services.AddHostedService<ScheduleRefreshService>();
        services.AddHostedService<UserStateCleanupService>();

        /// <summary>
        /// Registers health check for monitoring bot status.
        /// </summary>
        services.AddHealthChecks().AddCheck<BotHealthCheck>("bot_health");

        return services;
    }
}