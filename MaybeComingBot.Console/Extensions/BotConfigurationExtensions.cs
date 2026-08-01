using Microsoft.Extensions.Configuration;
using Serilog;

namespace MaybeComingBot.Extensions;

public static class BotConfigurationExtensions
{
    public static IConfiguration BuildBotConfiguration()
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";

        return new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();
    }

    public static void ConfigureBotSerilog(this IConfiguration configuration)
    {
        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .Enrich.FromLogContext()
            .CreateLogger();
    }

    public static IConfigurationSection GetBotSection(this IConfiguration configuration)
    {
        return configuration.GetSection("Bot");
    }

    public static Models.BotSettings? GetBotSettings(this IConfiguration configuration)
    {
        return configuration.GetSection("Bot").Get<Models.BotSettings>();
    }
}