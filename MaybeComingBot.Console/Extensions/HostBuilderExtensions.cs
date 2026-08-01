using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace MaybeComingBot.Extensions;

/// <summary>
/// Расширения для IHostBuilder
/// </summary>
public static class HostBuilderExtensions
{
    /// <summary>
    /// Настройка хоста с Serilog и конфигурацией
    /// </summary>
    public static IHostBuilder UseBotConfiguration(this IHostBuilder hostBuilder, string[] args)
    {
        return hostBuilder
            .UseSerilog()
            .ConfigureServices((context, services) =>
            {
                services.AddBotServices(context.Configuration);
            });
    }

    /// <summary>
    /// Добавление обработчиков завершения
    /// </summary>
    public static IHostBuilder AddShutdownHandlers(this IHostBuilder hostBuilder)
    {
        return hostBuilder.ConfigureServices((context, services) =>
        {
            // Можно добавить дополнительные обработчики при завершении
        });
    }
}