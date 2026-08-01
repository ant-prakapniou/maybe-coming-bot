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

Console.OutputEncoding = Encoding.UTF8;

// ============================================================
// 1. НАСТРОЙКА КОНФИГУРАЦИИ И ЛОГГЕРА
// ============================================================
var configuration = BotConfigurationExtensions.BuildBotConfiguration();
configuration.ConfigureBotSerilog();

try
{
    Log.Information("🚀 Bot initializing...");

    // ============================================================
    // 2. ПОСТРОЕНИЕ ХОСТА - используем метод расширения
    // ============================================================
    var host = Host.CreateDefaultBuilder(args)
        .UseSerilog()
        .ConfigureServices((context, services) =>
        {
            // ✅ ОДНА регистрация через метод расширения
            services.AddBotServices(context.Configuration);
        })
        .Build();

    // ============================================================
    // 3. ВАЛИДАЦИЯ И ЗАПУСК БОТА
    // ============================================================
    await ValidateConfigurationAndAdminAsync(host);
    await RunBotWithRestartAsync(host);

    await host.StopAsync(TimeSpan.FromSeconds(30));
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

// ============================================================
// 4. ЛОКАЛЬНЫЕ ФУНКЦИИ
// ============================================================

static async Task ValidateConfigurationAndAdminAsync(IHost host, CancellationToken cancellationToken = default)
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

static async Task RunBotWithRestartAsync(IHost host)
{
    var logger = host.Services.GetRequiredService<ILogger<Program>>();
    var botClient = host.Services.GetRequiredService<ITelegramBotClient>();
    var handler = host.Services.GetRequiredService<BotHandler>();

    var receiverOptions = new ReceiverOptions
    {
        AllowedUpdates = [UpdateType.Message, UpdateType.CallbackQuery]
    };

    var me = await botClient.GetMe();
    logger.LogInformation("🤖 Bot live: @{Username}", me.Username);
    logger.LogInformation("🔄 Press Ctrl+C to stop.");

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (s, e) =>
    {
        e.Cancel = true;
        logger.LogInformation("Shutdown requested...");
        cts.Cancel();
    };

    while (!cts.Token.IsCancellationRequested)
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
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Bot stopped by user.");
            break;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Bot crashed. Restarting in 10 seconds...");
            await Task.Delay(TimeSpan.FromSeconds(10), cts.Token);
        }
    }
}