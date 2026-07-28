using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Text;
using MaybeComingBot.Console.Models;
using MaybeComingBot.Console.Services;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace MaybeComingBot.Console;

public class Program
{
    private static readonly ConcurrentDictionary<long, UserState> UserStates = new();

    public static async Task Main(string[] args)
    {
        System.Console.OutputEncoding = Encoding.UTF8;

        using IHost host = Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                // 1. Привязываем секцию "Bot" к BotSettings
                services.Configure<BotSettings>(context.Configuration.GetSection("Bot"));

                // 2. Регистрируем HttpClient для ScheduleLoader
                services.AddHttpClient<ScheduleLoader>();

                // 3. Регистрируем хранилище данных как синглтон
                services.AddSingleton<ScheduleStorage>();

                // 4. Регистрируем загрузчик как скоуп
                services.AddScoped<ScheduleLoader>();

                // 5. Регистрируем TelegramBotClient (используем токен из BotSettings)
                services.AddSingleton<ITelegramBotClient>(provider =>
                {
                    var settings = provider.GetRequiredService<IOptions<BotSettings>>().Value;
                    return new TelegramBotClient(settings.BotToken);
                });

                // 6. Регистрируем BotHandler как скоуп
                services.AddScoped<BotHandler>();

                // 7. Регистрируем словарь состояний пользователей как синглтон
                services.AddSingleton(UserStates);
            })
            .Build();

        // Загружаем расписание при старте
        using (var scope = host.Services.CreateScope())
        {
            var loader = scope.ServiceProvider.GetRequiredService<ScheduleLoader>();
            var storage = scope.ServiceProvider.GetRequiredService<ScheduleStorage>();

            try
            {
                var schedule = await loader.LoadAsync();
                storage.Update(schedule);
                System.Console.WriteLine("[INFO] Schedule loaded successfully.");
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[ERROR] Failed to load initial schedule: {ex.Message}");
            }
        }

        // Получаем бота и обработчик
        var botClient = host.Services.GetRequiredService<ITelegramBotClient>();
        var handler = host.Services.GetRequiredService<BotHandler>();

        // Настраиваем получение обновлений
        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = [UpdateType.Message, UpdateType.CallbackQuery]
        };

        // Запускаем бота
        System.     Console.WriteLine("🚀 Бот запущен. Нажмите Ctrl+C для остановки.");

        try
        {
            using var cts = new CancellationTokenSource();
            System.Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
                System.Console.WriteLine("⏹️ Бот остановлен.");
            };

            // Начинаем получать обновления
            await botClient.ReceiveAsync(
                updateHandler: async (client, update, cancellationToken) =>
                {
                    try
                    {
                        await handler.HandleUpdateAsync(update);
                    }
                    catch (Exception ex)
                    {
                        System.Console.WriteLine($"[ERROR] Update handling error: {ex.Message}");
                    }
                },
                errorHandler: async (client, exception, cancellationToken) =>
                {
                    System.Console.WriteLine($"[ERROR] Polling error: {exception.Message}");
                    await Task.CompletedTask;
                },
                receiverOptions,
                cancellationToken: cts.Token
            );
        }
        catch (TaskCanceledException)
        {
            System.Console.WriteLine("⏹️ Бот остановлен.");
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"[FATAL] {ex.Message}");
            throw;
        }
        finally
        {
            System.Console.WriteLine("👋 Завершение работы...");
            await host.StopAsync();
        }
    }
}