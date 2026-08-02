using MaybeComingBot.Extensions;
using MaybeComingBot.Models;
using MaybeComingBot.Services;
using MaybeComingBot.Utils;
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

/// <summary>
/// The main entry point for the Telegram bot application.
/// </summary>
/// <remarks>
/// The application flow:
/// <list type="number">
/// <item><description>Configures logging and configuration</description></item>
/// <item><description>Builds the dependency injection container</description></item>
/// <item><description>Validates admin users</description></item>
/// <item><description>Starts the bot with automatic restart capability</description></item>
/// <item><description>Handles graceful shutdown on Ctrl+C</description></item>
/// </list>
/// </remarks>
public class Program
{
    /// <summary>
    /// The main entry point of the application.
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public static async Task Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        // ============================================================
        // 1. CONFIGURATION AND LOGGER SETUP
        // ============================================================

        var configuration = BotConfigurationExtensions.BuildBotConfiguration();
        configuration.ConfigureBotSerilog();

        try
        {
            Log.Information("🚀 Bot initializing...");

            // ============================================================
            // 2. HOST BUILDING
            // ============================================================

            using var host = Host.CreateDefaultBuilder(args)
                .UseSerilog()
                .ConfigureServices((context, services) =>
                {
                    services.AddBotServices(context.Configuration);
                })
                .Build();

            // ============================================================
            // 3. VALIDATION, HOST START, AND BOT EXECUTION
            // ============================================================

            await ValidateConfigurationAndAdminAsync(host);

            // Start hosted services (schedule refresh, user state cleanup)
            await host.StartAsync();

            try
            {
                await RunBotWithRestartAsync(host);
            }
            finally
            {
                // Stop host with timeout (graceful shutdown of hosted services)
                await host.StopAsync(TimeSpan.FromSeconds(30));
            }
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

    /// <summary>
    /// Validates that all configured admin users exist and are accessible.
    /// </summary>
    /// <param name="host">The host containing required services.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when an admin user ID is invalid or not found.
    /// </exception>
    private static async Task ValidateConfigurationAndAdminAsync(IHost host, CancellationToken cancellationToken = default)
    {
        var settings = host.Services.GetRequiredService<IOptions<BotSettings>>().Value;
        var botClient = host.Services.GetRequiredService<ITelegramBotClient>();
        var logger = host.Services.GetRequiredService<ILogger<Program>>();

        var adminIds = settings.AdminUserIds ?? Array.Empty<long>();

        logger.LogInformation("🔐 Validating admin users...");

        foreach (var adminId in adminIds)
        {
            try
            {
                var admin = await botClient.GetChat(adminId, cancellationToken);
                logger.LogInformation("✅ Admin: @{Username} (ID: {UserId})",
                    admin.Username ?? "NO_USERNAME", adminId);
            }
            catch (ApiRequestException ex) when (ex.ErrorCode == 400)
            {
                logger.LogError("❌ Admin ID {AdminUserId} invalid", adminId);
                throw new InvalidOperationException($"Admin {adminId} not found.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "❌ Admin validation failed for {AdminUserId}", adminId);
                throw;
            }
        }

        logger.LogInformation("✅ All admins validated");
    }

    /// <summary>
    /// Runs the bot with automatic restart capability on failure.
    /// </summary>
    /// <param name="host">The host containing required services.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// <para>
    /// This method:
    /// <list type="number">
    /// <item><description>Creates a cancellation token linked to application shutdown</description></item>
    /// <item><description>Handles Ctrl+C with proper cancellation</description></item>
    /// <item><description>Starts the Telegram polling loop</description></item>
    /// <item><description>Automatically restarts on crash (up to 5 attempts)</description></item>
    /// <item><description>Implements exponential backoff between restart attempts</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// The restart mechanism:
    /// <list type="bullet">
    /// <item><description>Each restart attempt increases the delay: 10s, 20s, 30s, 40s, 50s</description></item>
    /// <item><description>After 5 consecutive failures, the bot stops permanently</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    private static async Task RunBotWithRestartAsync(IHost host)
    {
        var logger = host.Services.GetRequiredService<ILogger<Program>>();
        var botClient = host.Services.GetRequiredService<ITelegramBotClient>();
        var handler = host.Services.GetRequiredService<BotHandler>();
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = [UpdateType.Message, UpdateType.CallbackQuery]
        };

        var me = await botClient.GetMe();
        logger.LogInformation("🤖 Bot live: @{Username}", me.Username);
        logger.LogInformation("🔄 Press Ctrl+C to stop.");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(
            lifetime.ApplicationStopping,
            CancellationToken.None
        );

        // Handle Ctrl+C with proper cancellation
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            logger.LogInformation("Shutdown requested...");
            cts.Cancel();
        };

        int restartAttempts = 0;
        const int maxRestartAttempts = 5;

        while (!cts.Token.IsCancellationRequested && restartAttempts < maxRestartAttempts)
        {
            try
            {
                await botClient.ReceiveAsync(
                    updateHandler: async (_, update, _) =>
                    {
                        try
                        {
                            await handler.HandleUpdateAsync(update);
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Update handling error");
                        }
                    },
                    errorHandler: (_, exception, _) =>
                    {
                        if (exception is ApiRequestException apiEx)
                            logger.LogWarning(apiEx, "Polling error (Code: {ErrorCode})", apiEx.ErrorCode);
                        else
                            logger.LogError(exception, "Polling error");
                        return Task.CompletedTask;
                    },
                    receiverOptions,
                    cancellationToken: cts.Token
                );

                restartAttempts = 0;
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("Bot stopped by user.");
                break;
            }
            catch (Exception ex)
            {
                restartAttempts++;
                logger.LogError(ex, "Bot crashed (attempt {Attempt}/{MaxAttempts}). Restarting in {Delay} seconds...",
                    restartAttempts, maxRestartAttempts, 10 * restartAttempts);

                if (restartAttempts >= maxRestartAttempts)
                {
                    logger.LogCritical("❌ Maximum restart attempts reached. Bot will not restart.");
                    throw;
                }

                await Task.Delay(TimeSpan.FromSeconds(10 * restartAttempts), cts.Token);
            }
        }

        if (restartAttempts >= maxRestartAttempts)
        {
            throw new InvalidOperationException($"Bot stopped after {maxRestartAttempts} failed restart attempts.");
        }
    }
}