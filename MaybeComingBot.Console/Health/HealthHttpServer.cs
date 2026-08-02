using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace MaybeComingBot.Health;

/// <summary>
/// Lightweight ASP.NET Core (Kestrel) health-check endpoint for container platforms
/// such as Railway. Runs a minimal <see cref="WebApplication"/> bound to the <c>PORT</c>
/// environment variable (default 8080) and answers <c>GET /health</c> (plus <c>/healthz</c>
/// and <c>/</c>) with the current bot health status.
/// </summary>
/// <remarks>
/// The app is a console worker, not an ASP.NET host, so this <see cref="BackgroundService"/>
/// hosts a small Kestrel <see cref="WebApplication"/> in the background without disturbing
/// the Telegram polling loop. Healthy or degraded states return HTTP 200; unhealthy returns
/// 503. If the port cannot be bound, the bot logs a warning and keeps running.
/// </remarks>
public class HealthHttpServer(
    HealthCheckService healthCheckService,
    ILogger<HealthHttpServer> logger) : BackgroundService
{
    private const int DefaultPort = 8080;

    private readonly HealthCheckService _healthCheckService = healthCheckService;
    private readonly ILogger<HealthHttpServer> _logger = logger;

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var port = GetPort();
        var urls = $"http://0.0.0.0:{port}";

        WebApplication app;
        try
        {
            var builder = WebApplication.CreateSlimBuilder();

            // Route Kestrel logs through the same Serilog pipeline as the bot.
            builder.Logging.ClearProviders();
            builder.Logging.AddSerilog(Log.Logger);
            builder.WebHost.UseUrls(urls);

            // Reuse the outer generic host's HealthCheckService so the BotHealthCheck
            // registered by AddBotServices is evaluated by this minimal app too.
            builder.Services.AddSingleton(_healthCheckService);

            app = builder.Build();

            var healthCheckOptions = new HealthCheckOptions
            {
                ResponseWriter = WriteHealthReportAsync
            };

            app.MapHealthChecks("/health", healthCheckOptions);
            app.MapHealthChecks("/healthz", healthCheckOptions);
            app.MapHealthChecks("/", healthCheckOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to build the health server. The bot will continue running " +
                "without an HTTP endpoint.");
            return;
        }

        try
        {
            _logger.LogInformation("🩺 Health server starting on port {Port}", port);
            await app.RunAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to start health server on port {Port}. The bot will continue running " +
                "without an HTTP endpoint (set PORT to a free port to enable it).", port);
        }
        finally
        {
            await app.DisposeAsync();
            _logger.LogInformation("Health server stopped");
        }
    }

    /// <summary>
    /// Writes the health report as JSON in the same shape as the original
    /// listener-based implementation: lowercase status plus per-check details.
    /// </summary>
    private static async Task WriteHealthReportAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var body = JsonSerializer.Serialize(new
        {
            status = report.Status.ToString().ToLowerInvariant(),
            checks = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => new
                {
                    status = entry.Value.Status.ToString().ToLowerInvariant(),
                    description = entry.Value.Description,
                    data = entry.Value.Data
                })
        });

        await context.Response.WriteAsync(body, context.RequestAborted);
    }

    private static int GetPort()
    {
        var raw = Environment.GetEnvironmentVariable("PORT");
        if (int.TryParse(raw, out var port) && port is > 0 and < 65536)
            return port;
        return DefaultPort;
    }
}

