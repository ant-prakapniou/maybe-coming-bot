using MaybeComingBot.Extensions;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace MaybeComingBot.Infrastructure;

/// <summary>
/// Построитель хоста для бота
/// </summary>
public static class BotHostBuilder
{
    /// <summary>
    /// Создание IHostBuilder с базовой конфигурацией
    /// </summary>
    public static IHostBuilder CreateHostBuilder(string[] args)
    {
        return Host.CreateDefaultBuilder(args)
            .UseSerilog()
            .ConfigureServices((context, services) =>
            {
                services.AddBotServices(context.Configuration);
            });
    }

    /// <summary>
    /// Создание IHostBuilder с расширенной конфигурацией
    /// </summary>
    public static IHostBuilder CreateHostBuilderWithExtensions(string[] args)
    {
        return Host.CreateDefaultBuilder(args)
            .UseBotConfiguration(args)
            .AddShutdownHandlers();
    }
}