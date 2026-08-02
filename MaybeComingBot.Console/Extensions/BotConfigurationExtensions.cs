using Microsoft.Extensions.Configuration;
using Serilog;
using MaybeComingBot.Models;
using System.ComponentModel.DataAnnotations;

namespace MaybeComingBot.Extensions;

/// <summary>
/// Provides extension methods for configuration building and bot settings management.
/// </summary>
/// <remarks>
/// This class centralizes all configuration-related operations including:
/// <list type="bullet">
/// <item><description>Building configuration from JSON files and environment variables</description></item>
/// <item><description>Configuring Serilog logging</description></item>
/// <item><description>Retrieving and validating <see cref="BotSettings"/></description></item>
/// </list>
/// </remarks>
public static class BotConfigurationExtensions
{
    /// <summary>
    /// Builds the application configuration from multiple sources.
    /// </summary>
    /// <returns>
    /// An <see cref="IConfiguration"/> instance containing merged configuration.
    /// </returns>
    /// <exception cref="FileNotFoundException">
    /// Thrown when the required 'appsettings.json' file is not found.
    /// </exception>
    /// <remarks>
    /// Configuration is loaded from the following sources in order:
    /// <list type="number">
    /// <item><description>appsettings.json (required)</description></item>
    /// <item><description>appsettings.{environment}.json (optional, environment-specific)</description></item>
    /// <item><description>Environment variables (override all)</description></item>
    /// </list>
    /// The environment is determined by the ASPNETCORE_ENVIRONMENT variable, defaulting to "Production".
    /// </remarks>
    public static IConfiguration BuildBotConfiguration()
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
        var basePath = Directory.GetCurrentDirectory();
        var configPath = Path.Combine(basePath, "appsettings.json");

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

    /// <summary>
    /// Configures Serilog global logger from the provided configuration.
    /// </summary>
    /// <param name="configuration">The configuration containing Serilog settings.</param>
    /// <remarks>
    /// This method initializes the static <see cref="Log.Logger"/> instance.
    /// It should be called once at application startup, typically before building the host.
    /// </remarks>
    public static void ConfigureBotSerilog(this IConfiguration configuration)
    {
        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .Enrich.FromLogContext()
            .CreateLogger();
    }

    /// <summary>
    /// Retrieves the BotSettings configuration section.
    /// </summary>
    /// <param name="configuration">The configuration instance.</param>
    /// <returns>
    /// An <see cref="IConfigurationSection"/> representing the BotSettings section.
    /// </returns>
    public static IConfigurationSection GetBotSection(this IConfiguration configuration)
        => configuration.GetSection(nameof(BotSettings));

    /// <summary>
    /// Retrieves and validates the BotSettings from configuration.
    /// </summary>
    /// <param name="configuration">The configuration instance.</param>
    /// <returns>
    /// A validated <see cref="BotSettings"/> instance.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when:
    /// <list type="bullet">
    /// <item><description>The BotSettings section is missing from configuration</description></item>
    /// <item><description>DataAnnotation validation fails for any property</description></item>
    /// </list>
    /// </exception>
    /// <remarks>
    /// This method performs both structural validation (section exists) and
    /// DataAnnotation validation (property constraints).
    /// </remarks>
    public static BotSettings GetBotSettings(this IConfiguration configuration)
    {
        var section = configuration.GetSection(nameof(BotSettings));
        var settings = section.Get<BotSettings>()??throw new InvalidOperationException(
                $"BotSettings section is missing or empty in configuration. " +
                $"Please add 'BotSettings' section to appsettings.json.");

        var validationResults = new List<ValidationResult>();
        var context = new ValidationContext(settings);

        if (!Validator.TryValidateObject(settings, context, validationResults, true))
        {
            var errors = string.Join(", ", validationResults.Select(v => v.ErrorMessage));
            throw new InvalidOperationException($"Invalid BotSettings configuration: {errors}");
        }

        return settings;
    }
}