using MaybeComingBot.Abstractions;
using MaybeComingBot.Models;
using MaybeComingBot.Services;
using MaybeComingBot.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
            .WriteTo.File(
                "logs/bot-.txt",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .MinimumLevel.Information()
            .CreateLogger();

        try
        {
            Log.Information("🚀 Bot starting...");

            var host = CreateHostBuilder(args).Build();

            await ValidateConfigurationAndAdminAsync(host);

            await LoadScheduleAsync(host);

            await RunBotAsync(host);
            await host.WaitForShutdownAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Bot terminated unexpectedly");
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
                    .AddPolicyHandler(PollyPolicyExtensions.GetRetryPolicy())
                    .AddPolicyHandler(PollyPolicyExtensions.GetTimeoutPolicy())
                    .SetHandlerLifetime(TimeSpan.FromMinutes(5));

                services.AddSingleton<ITelegramBotClient>(provider =>
                {
                    var settings = provider.GetRequiredService<IOptions<BotSettings>>().Value;
                    return new TelegramBotClient(settings.BotToken);
                });

                services.AddSingleton<IValidateOptions<BotSettings>, BotSettingsValidator>();
                services.AddSingleton<ScheduleStorage>();
                services.AddSingleton<RateLimiter>();
                services.AddSingleton<IUserStateService, UserStateService>();

                services.AddSingleton<ScheduleRefreshService>();
                services.AddHostedService(provider => provider.GetRequiredService<ScheduleRefreshService>());

                services.AddSingleton<BotHandler>();

                services.AddHostedService<UserStateCleanupService>();
            });
    }

    private static async Task ValidateConfigurationAndAdminAsync(IHost host)
    {
        var settings = host.Services.GetRequiredService<IOptions<BotSettings>>().Value;
        var botClient = host.Services.GetRequiredService<ITelegramBotClient>();
        var logger = Log.ForContext<Program>();

        logger.Information("Validating configuration...");

        try
        {
            var admin = await botClient.GetChat(settings.AdminUserId);
            logger.Information("✅ Admin found: @{Username} (ID: {UserId})",
                admin.Username ?? "no_username",
                settings.AdminUserId);
        }
        catch (ApiRequestException ex) when (ex.ErrorCode == 400)
        {
            logger.Error("❌ AdminUserId {AdminUserId} does not exist", settings.AdminUserId);
            throw new InvalidOperationException(
                $"Admin user with ID {settings.AdminUserId} not found.");
        }
        catch (Exception ex)
        {
            logger.Error(ex, "❌ Failed to validate admin user");
            throw;
        }
    }

    private static async Task LoadScheduleAsync(IHost host)
    {
        var logger = Log.ForContext<Program>();
        logger.Information("⏳ Loading schedule before bot start...");

        try
        {
            using var scope = host.Services.CreateScope();
            var storage = scope.ServiceProvider.GetRequiredService<ScheduleStorage>();
            var loader = scope.ServiceProvider.GetRequiredService<ScheduleLoader>();

            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    logger.Information("Attempt {Attempt}/3 loading schedule...", attempt);
                    var schedule = await loader.LoadAsync();

                    logger.Information("Schedule loaded: Stops={Count}, IsNull={IsNull}",
                        schedule?.Stops?.Count ?? 0, schedule is null);

                    if (schedule?.Stops is not null && schedule.Stops.Count > 0)
                    {
                        storage.Update(schedule);

                        var hasData = storage.HasData();
                        var stopCount = storage.StopCount;
                        var allStops = storage.GetAllStops().ToList();

                        logger.Information("✅ After Update: HasData={HasData}, StopCount={StopCount}, GetAllStops.Count={Count}",
                            hasData, stopCount, allStops.Count);

                        return;
                    }

                    logger.Warning("Schedule loaded but empty (attempt {Attempt})", attempt);
                }
                catch (Exception ex)
                {
                    logger.Warning(ex, "Load attempt {Attempt} failed", attempt);
                }

                if (attempt < 3)
                {
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                    logger.Information("Retrying in {Delay}s...", delay.TotalSeconds);
                    await Task.Delay(delay);
                }
            }

            throw new InvalidOperationException(
                "Failed to load schedule after 3 attempts. " +
                "Check JsonUrl in appsettings.json and network connectivity.");
        }
        catch (Exception ex)
        {
            logger.Error(ex, "❌ CRITICAL: Failed to load schedule. Bot will not start.");
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
        logger.Information("🤖 Bot started: @{Username}", me.Username);
        logger.Information("🔄 Press Ctrl+C to stop.");

        using var cts = new CancellationTokenSource();

        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
            logger.Information("Shutdown signal received");
        };

        while (!cts.Token.IsCancellationRequested)
        {
            try
            {
                await botClient.ReceiveAsync(
                    updateHandler: async (client, update, cancellationToken) =>
                    {
                        try
                        {
                            await handler.HandleUpdateAsync(update);
                        }
                        catch (Exception ex)
                        {
                            logger.Error(ex, "Error handling update");
                        }
                    },
                    errorHandler: (client, exception, cancellationToken) =>
                    {
                        if (exception is ApiRequestException apiEx)
                        {
                            logger.Warning(apiEx, "Polling error: {Message}", apiEx.Message);
                        }
                        else
                        {
                            logger.Error(exception, "Polling error");
                        }
                        return Task.CompletedTask;
                    },
                    receiverOptions,
                    cancellationToken: cts.Token
                );
            }
            catch (OperationCanceledException)
            {
                logger.Information("Bot stopped gracefully");
                break;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Bot crashed. Restarting in 5 seconds...");
                await Task.Delay(5000, cts.Token);
            }
        }
    }
}