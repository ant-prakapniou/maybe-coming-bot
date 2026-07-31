using MaybeComingBot.Abstractions;
using MaybeComingBot.Models;
using MaybeComingBot.Services;
using MaybeComingBot.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using System.Text;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;

namespace MaybeComingBot;

public class Program
{
    public static async Task Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .WriteTo.File("logs/bot-.txt", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .MinimumLevel.Information()
            .CreateLogger();

        try
        {
            Log.Information("🚀 Bot initializing...");
            var host = CreateHostBuilder(args).Build();
            await ValidateConfigurationAndAdminAsync(host);
            await RunBotAsync(host);
            await host.WaitForShutdownAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Bot crashed.");
            throw;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static IHostBuilder CreateHostBuilder(string[] args)
    {
        return Host.CreateDefaultBuilder(args)
            .ConfigureHostConfiguration(config =>
            {
                config.SetBasePath(Directory.GetCurrentDirectory());
                config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                config.AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true);
                config.AddEnvironmentVariables();
            })
            .UseSerilog()
            .ConfigureServices((context, services) =>
            {
                services.Configure<BotSettings>(context.Configuration.GetSection("Bot"));

                services.AddOptions<BotSettings>()
                    .Bind(context.Configuration.GetSection("Bot"))
                    .ValidateDataAnnotations()
                    .ValidateOnStart();

                services.AddHttpClient<ScheduleLoader>()
                    .AddPolicyHandler((provider, _) =>
                    {
                        var settings = provider.GetRequiredService<IOptions<BotSettings>>();
                        return PollyPolicyExtensions.GetRetryPolicy(settings);
                    })
                    .AddPolicyHandler(PollyPolicyExtensions.GetTimeoutPolicy())
                    .SetHandlerLifetime(TimeSpan.FromMinutes(5));

                services.AddSingleton<ITelegramBotClient>(provider =>
                {
                    var settings = provider.GetRequiredService<IOptions<BotSettings>>().Value;
                    return new TelegramBotClient(settings.BotToken);
                });

                services.AddSingleton<IValidateOptions<BotSettings>, BotSettingsValidator>();
                services.AddSingleton<IScheduleStorage, ScheduleStorage>();
                services.AddSingleton<RateLimiter>();
                services.AddSingleton<IUserStateService, UserStateService>();

                services.AddSingleton<TelegramSender>();
                services.AddSingleton<StopSelectionGuard>();
                services.AddSingleton<ScheduleFormatter>();
                services.AddSingleton<CommandDispatcher>(provider =>
                {
                    var settings = provider.GetRequiredService<IOptions<BotSettings>>().Value;
                    return new CommandDispatcher(
                        provider.GetRequiredService<ITelegramBotClient>(),
                        provider.GetRequiredService<IScheduleStorage>(),
                        provider.GetRequiredService<IUserStateService>(),
                        provider.GetRequiredService<ScheduleRefreshService>(),
                        provider.GetRequiredService<ScheduleFormatter>(),
                        provider.GetRequiredService<StopSelectionGuard>(),
                        provider.GetRequiredService<TelegramSender>(),
                        settings.AdminUserId,
                        provider.GetRequiredService<ILogger<CommandDispatcher>>());
                });

                services.AddSingleton<BotHandler>();

                services.AddSingleton<ScheduleRefreshService>();
                services.AddHostedService(provider => provider.GetRequiredService<ScheduleRefreshService>());

                services.AddHostedService<UserStateCleanupService>();
            });
    }

    private static async Task ValidateConfigurationAndAdminAsync(IHost host)
    {
        var settings = host.Services.GetRequiredService<IOptions<BotSettings>>().Value;
        var botClient = host.Services.GetRequiredService<ITelegramBotClient>();
        var logger = Log.ForContext<Program>();

        try
        {
            var admin = await botClient.GetChat(settings.AdminUserId);
            logger.Information("✅ Admin: @{Username} (ID: {UserId})",
                admin.Username ?? "NO_USERNAME", settings.AdminUserId);
        }
        catch (ApiRequestException ex) when (ex.ErrorCode == 400)
        {
            logger.Error("❌ Admin ID {AdminUserId} invalid", settings.AdminUserId);
            throw new InvalidOperationException($"Admin {settings.AdminUserId} not found.");
        }
        catch (Exception ex)
        {
            logger.Error(ex, "❌ Admin validation failed");
            throw;
        }
    }

    private static async Task RunBotAsync(IHost host)
    {
        var logger = Log.ForContext<Program>();
        var botClient = host.Services.GetRequiredService<ITelegramBotClient>();
        var handler = host.Services.GetRequiredService<BotHandler>();

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = [UpdateType.Message, UpdateType.CallbackQuery]
        };

        var me = await botClient.GetMe();
        logger.Information("🤖 Bot live: @{Username}", me.Username);
        logger.Information("🔄 Press Ctrl+C to stop.");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) => { e.Cancel = true; cts.Cancel(); };

        try
        {
            await botClient.ReceiveAsync(
                updateHandler: async (_, update, _) =>
                {
                    try { await handler.HandleUpdateAsync(update); }
                    catch (Exception ex) { logger.Error(ex, "Update error"); }
                },
                errorHandler: (_, exception, _) =>
                {
                    if (exception is ApiRequestException apiEx)
                        logger.Warning(apiEx, "Polling error");
                    else
                        logger.Error(exception, "Polling error");
                    return Task.CompletedTask;
                },
                receiverOptions,
                cancellationToken: cts.Token
            );
        }
        catch (OperationCanceledException)
        {
            logger.Information("Bot stopped.");
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Bot crashed.");
            throw;
        }
    }
}