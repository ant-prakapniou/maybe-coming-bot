using Microsoft.Extensions.Configuration;
using Serilog;
using MaybeComingBot.Models;

namespace MaybeComingBot.Extensions;

public static class BotConfigurationExtensions
{
    public static IConfiguration BuildBotConfiguration()
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
        var basePath = Directory.GetCurrentDirectory();
        var configPath = Path.Combine(basePath, "appsettings.json");

        // Проверяем наличие основного файла конфигурации
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException(
                $"Configuration file 'appsettings.json' not found at '{configPath}'. " +
                "Please create it with required settings.");
        }

        return new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: true)
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
        return configuration.GetSection(nameof(BotSettings));
    }

    public static BotSettings GetBotSettings(this IConfiguration configuration)
    {
        var section = configuration.GetSection(nameof(BotSettings));
        var settings = section.Get<BotSettings>();

        if (settings is null)
        {
            throw new InvalidOperationException(
                $"BotSettings section is missing or empty in configuration. " +
                $"Please add 'BotSettings' section to appsettings.json.");
        }

        // Валидация через DataAnnotations
        var validationResults = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var context = new System.ComponentModel.DataAnnotations.ValidationContext(settings);

        if (!System.ComponentModel.DataAnnotations.Validator.TryValidateObject(settings, context, validationResults, true))
        {
            var errors = string.Join(", ", validationResults.Select(v => v.ErrorMessage));
            throw new InvalidOperationException($"Invalid BotSettings configuration: {errors}");
        }

        return settings;
    }
}