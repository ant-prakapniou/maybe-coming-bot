using MaybeComingBot.Extensions;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace MaybeComingBot.Infrastructure;

/// <summary>
/// Provides factory methods for building and configuring the bot host.
/// </summary>
/// <remarks>
/// This class encapsulates host creation logic with different configuration strategies.
/// It serves as the entry point for host setup in the application.
/// </remarks>
public static class BotHostBuilder
{
    /// <summary>
    /// Creates an <see cref="IHostBuilder"/> with basic configuration.
    /// </summary>
    /// <param name="args">Command line arguments passed to the application.</param>
    /// <returns>
    /// An <see cref="IHostBuilder"/> instance with Serilog and bot services configured.
    /// </returns>
    /// <remarks>
    /// This method provides a minimal host configuration:
    /// <list type="bullet">
    /// <item><description>Uses Serilog as the logging provider</description></item>
    /// <item><description>Registers all bot services via <see cref="ServiceCollectionExtensions.AddBotServices"/></description></item>
    /// </list>
    /// </remarks>
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
    /// Creates an <see cref="IHostBuilder"/> with extended configuration using custom extensions.
    /// </summary>
    /// <param name="args">Command line arguments passed to the application.</param>
    /// <returns>
    /// An <see cref="IHostBuilder"/> instance with full bot configuration applied.
    /// </returns>
    /// <remarks>
    /// This method provides a more comprehensive host configuration:
    /// <list type="bullet">
    /// <item><description>Uses <see cref="HostBuilderExtensions.UseBotConfiguration"/> for setup</description></item>
    /// <item><description>Adds shutdown handlers via <see cref="HostBuilderExtensions.AddShutdownHandlers"/></description></item>
    /// </list>
    /// Use this method when you need the full configuration stack including additional handlers.
    /// </remarks>
    public static IHostBuilder CreateHostBuilderWithExtensions(string[] args)
    {
        return Host.CreateDefaultBuilder(args)
            .UseBotConfiguration(args)
            .AddShutdownHandlers();
    }
}