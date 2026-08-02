using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MaybeComingBot.Health;

/// <summary>
/// Minimal, dependency-free HTTP health-check server for container platforms
/// such as Railway. Binds a <see cref="TcpListener"/> to the <c>PORT</c>
/// environment variable (default 8080) and answers <c>GET /health</c> (plus
/// <c>/healthz</c> and <c>/</c>) with the current bot health status.
/// </summary>
/// <remarks>
/// The app is a console worker, not an ASP.NET host, so this small listener
/// avoids pulling in the full ASP.NET framework. It uses only base-runtime
/// types and works on both Windows (local dev) and Linux (containers).
/// If the port cannot be bound, the bot logs a warning and keeps running.
/// </remarks>
public class HealthHttpServer(
    HealthCheckService healthCheckService,
    ILogger<HealthHttpServer> logger) : BackgroundService
{
    private const int DefaultPort = 8080;
    private const int MaxHeaderBytes = 16 * 1024;

    private static readonly byte[] NotFoundBody = Encoding.UTF8.GetBytes("Not Found");
    private static readonly byte[] HeaderTooLargeBody = Encoding.UTF8.GetBytes("Request Header Fields Too Large");

    private readonly HealthCheckService _healthCheckService = healthCheckService;
    private readonly ILogger<HealthHttpServer> _logger = logger;

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var port = GetPort();
        TcpListener? listener = null;

        try
        {
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            _logger.LogInformation("🩺 Health server listening on port {Port}", port);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to start health server on port {Port}. The bot will continue running " +
                "without an HTTP endpoint (set PORT to a free port to enable it).", port);
            return;
        }

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                Socket socket;
                try
                {
                    socket = await listener.Server.AcceptAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (SocketException ex)
                {
                    _logger.LogDebug(ex, "Health server accept failed");
                    continue;
                }

                var client = new TcpClient { Client = socket };
                _ = Task.Run(() => HandleClientAsync(client, stoppingToken), stoppingToken);
            }
        }
        finally
        {
            listener.Stop();
            _logger.LogInformation("Health server stopped");
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        using (var stream = client.GetStream())
        {
            try
            {
                var headerBytes = new List<byte>(256);
                var buffer = new byte[1024];
                var headerEnd = -1;

                while (headerEnd < 0)
                {
                    var read = await stream.ReadAsync(buffer, ct);
                    if (read == 0)
                        return;

                    headerBytes.AddRange(buffer[..read]);

                    if (headerBytes.Count > MaxHeaderBytes)
                    {
                        await WriteAsync(stream, BuildResponse(
                            431, "Request Header Fields Too Large", "text/plain", HeaderTooLargeBody), ct);
                        return;
                    }

                    headerEnd = FindHeaderEnd(headerBytes);
                }

                var requestLine = GetRequestLine(headerBytes);
                var path = GetPath(requestLine);

                if (path is "/health" or "/healthz" or "/")
                {
                    var result = await _healthCheckService.CheckHealthAsync(ct);
                    var isUp = result.Status is HealthStatus.Healthy or HealthStatus.Degraded;
                    var statusCode = isUp ? 200 : 503;

                    var body = JsonSerializer.Serialize(new
                    {
                        status = result.Status.ToString().ToLowerInvariant(),
                        checks = result.Entries?.ToDictionary(
                            entry => entry.Key,
                            entry => new
                            {
                                status = entry.Value.Status.ToString().ToLowerInvariant(),
                                description = entry.Value.Description,
                                data = entry.Value.Data
                            })
                    });

                    var bodyBytes = Encoding.UTF8.GetBytes(body);
                    await WriteAsync(stream, BuildResponse(
                        statusCode,
                        isUp ? "OK" : "Service Unavailable",
                        "application/json",
                        bodyBytes), ct);
                }
                else
                {
                    await WriteAsync(stream, BuildResponse(404, "Not Found", "text/plain", NotFoundBody), ct);
                }
            }
            catch (OperationCanceledException)
            {
                // Shutdown in progress.
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Health request handling failed");
            }
        }
    }

    private static async Task WriteAsync(NetworkStream stream, byte[] response, CancellationToken ct)
    {
        await stream.WriteAsync(response, ct);
        stream.Close();
    }

    private static byte[] BuildResponse(int statusCode, string reason, string contentType, byte[] body)
    {
        var header = $"HTTP/1.1 {statusCode} {reason}\r\n" +
                     $"Content-Type: {contentType}\r\n" +
                     $"Content-Length: {body.Length}\r\n" +
                     "Connection: close\r\n\r\n";
        var headerBytes = Encoding.ASCII.GetBytes(header);

        var response = new byte[headerBytes.Length + body.Length];
        Buffer.BlockCopy(headerBytes, 0, response, 0, headerBytes.Length);
        Buffer.BlockCopy(body, 0, response, headerBytes.Length, body.Length);
        return response;
    }

    private static int FindHeaderEnd(List<byte> bytes)
    {
        for (var i = 0; i <= bytes.Count - 4; i++)
        {
            if (bytes[i] == (byte)'\r' && bytes[i + 1] == (byte)'\n' &&
                bytes[i + 2] == (byte)'\r' && bytes[i + 3] == (byte)'\n')
            {
                return i + 4;
            }
        }
        return -1;
    }

    private static string GetRequestLine(List<byte> headerBytes)
    {
        var end = headerBytes.IndexOf((byte)'\r');
        if (end < 0)
            end = headerBytes.Count;
        return Encoding.ASCII.GetString(headerBytes.ToArray(), 0, end);
    }

    private static string GetPath(string requestLine)
    {
        var parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return "/";

        var path = parts[1];
        var queryIndex = path.IndexOf('?');
        if (queryIndex >= 0)
            path = path[..queryIndex];
        return path;
    }

    private static int GetPort()
    {
        var raw = Environment.GetEnvironmentVariable("PORT");
        if (int.TryParse(raw, out var port) && port is > 0 and < 65536)
            return port;
        return DefaultPort;
    }
}

