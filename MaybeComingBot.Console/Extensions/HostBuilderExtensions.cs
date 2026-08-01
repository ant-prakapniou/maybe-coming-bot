using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace MaybeComingBot.Extensions;

/// <summary>
/// Provides extension methods for configuring the <see cref="IHostBuilder"/>.
/// </summary>
/// <remarks>
/// These extensions simplify the host configuration process by encapsulating
/// common setup tasks such as Serilog integration and service registration.
/// </remarks>
public static class HostBuilderExtensions
{
    /// <summary>
    /// Configures the host with Serilog and bot services.
    /// </summary>
    /// <param name="hostBuilder">The host builder instance.</param>
    /// <param name="args">Command line arguments passed to the application.</param>
    /// <returns>
    /// The same <see cref="IHostBuilder"/> instance for method chaining.
    /// </returns>
    /// <remarks>
    /// This method performs the following configuration:
    /// <list type="bullet">
    /// <item><description>Adds Serilog as the logging provider</description></item>
    /// <item><description>Registers all bot services via <see cref="ServiceCollectionExtensions.AddBotServices"/></description></item>
    /// </list>
    /// </remarks>
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
    /// Adds shutdown handlers to the host.
    /// </summary>
    /// <param name="hostBuilder">The host builder instance.</param>
    /// <returns>
    /// The same <see cref="IHostBuilder"/> instance for method chaining.
    /// </returns>
    /// <remarks>
    /// This method provides a hook for adding custom shutdown handling logic.
    /// Currently serves as an extension point for future enhancements.
    /// </remarks>
    public static IHostBuilder AddShutdownHandlers(this IHostBuilder hostBuilder)
    {
        return hostBuilder.ConfigureServices((context, services) =>
        {
            // Extension point for additional shutdown handlers
            // Can be implemented in the future if needed
        });
    }
}