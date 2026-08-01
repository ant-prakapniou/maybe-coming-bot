using MaybeComingBot.Abstractions;
using MaybeComingBot.Models;
using MaybeComingBot.Services;
using MaybeComingBot.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Telegram.Bot;

namespace MaybeComingBot.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBotServices(this IServiceCollection services, IConfiguration configuration)
    {
        // ============================================================
        // 1. НАСТРОЙКИ
        // ============================================================
        services.AddOptions<BotSettings>()
            .Bind(configuration.GetBotSection())
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<BotSettings>, BotSettingsValidator>();

        // ============================================================
        // 2. HTTP CLIENT
        // ============================================================
        services.AddHttpClient(nameof(ScheduleLoader), client =>
        {
            var settings = configuration.GetBotSettings();
            if (settings is not null && !string.IsNullOrEmpty(settings.JsonUrl))
            {
                client.BaseAddress = new Uri(settings.JsonUrl);
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

        // ============================================================
        // 3. TELEGRAM CLIENT
        // ============================================================
        services.AddSingleton<ITelegramBotClient>(provider =>
        {
            var settings = provider.GetRequiredService<IOptions<BotSettings>>().Value;
            return new TelegramBotClient(settings.BotToken);
        });

        // ============================================================
        // 4. СЕРВИСЫ
        // ============================================================
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

        // ============================================================
        // 5. HOSTED SERVICES
        // ============================================================
        services.AddHostedService<ScheduleRefreshService>();
        services.AddHostedService<UserStateCleanupService>();

        // ============================================================
        // 6. HEALTH CHECKS
        // ============================================================
        services.AddHealthChecks().AddCheck<BotHealthCheck>("bot_health");

        return services;
    }
}