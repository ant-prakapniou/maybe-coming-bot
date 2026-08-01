using MaybeComingBot.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

namespace MaybeComingBot.Services;

/// <summary>
/// Loads schedule data from a remote JSON endpoint.
/// </summary>
/// <remarks>
/// This class is responsible for fetching and deserializing schedule data
/// from the configured URL. It includes timeout handling, error logging,
/// and detailed diagnostics for monitoring performance.
/// </remarks>
public class ScheduleLoader
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ScheduleLoader> _logger;
    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);
    private const string HttpClientName = nameof(ScheduleLoader);

    /// <summary>
    /// JSON serializer options for schedule deserialization.
    /// </summary>
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="ScheduleLoader"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory for creating HTTP clients.</param>
    /// <param name="logger">The logger instance for this class.</param>
    public ScheduleLoader(
        IHttpClientFactory httpClientFactory,
        ILogger<ScheduleLoader> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Loads schedule data from the configured URL.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>
    /// A <see cref="ScheduleRoot"/> containing the loaded schedule data.
    /// Never returns null; returns an empty schedule if no data is available.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the BaseAddress is not configured or empty.
    /// </exception>
    /// <exception cref="TimeoutException">
    /// Thrown when the request exceeds the configured timeout.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The loading process includes:
    /// <list type="number">
    /// <item><description>Validating BaseAddress configuration</description></item>
    /// <item><description>Making an HTTP GET request with timeout</description></item>
    /// <item><description>Checking for empty responses</description></item>
    /// <item><description>Deserializing JSON with relaxed options (case-insensitive, trailing commas)</description></item>
    /// <item><description>Logging performance metrics (duration, size)</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// All errors are caught and re-thrown with appropriate exception types
    /// for proper handling by the caller.
    /// </para>
    /// </remarks>
    public async Task<ScheduleRoot> LoadAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);

            // Validate BaseAddress
            if (client.BaseAddress is null || string.IsNullOrWhiteSpace(client.BaseAddress.ToString()))
            {
                _logger.LogError("BaseAddress not configured or empty for ScheduleLoader HttpClient");
                throw new InvalidOperationException(
                    "ScheduleLoader HttpClient BaseAddress is not configured or empty. " +
                    "Please set JsonUrl in BotSettings.");
            }

            _logger.LogDebug("Loading schedule from {BaseAddress}", client.BaseAddress);

            // Apply timeout
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_timeout);

            // Use GetAsync for better control
            var response = await client.GetAsync(string.Empty, cts.Token);
            response.EnsureSuccessStatusCode();

            // Check for empty response
            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength.HasValue && contentLength.Value == 0)
            {
                _logger.LogWarning("Empty response from server (Content-Length: 0)");
                return new ScheduleRoot { Stops = [] };
            }

            // Read and parse JSON
            var json = await response.Content.ReadAsStringAsync(cts.Token);

            if (string.IsNullOrWhiteSpace(json))
            {
                _logger.LogWarning("Empty JSON response from server");
                return new ScheduleRoot { Stops = [] };
            }

            var schedule = JsonSerializer.Deserialize<ScheduleRoot>(json, _jsonOptions);

            if (schedule is null || schedule.Stops is null)
            {
                _logger.LogWarning("Schedule response is null or invalid");
                return new ScheduleRoot { Stops = [] };
            }

            stopwatch.Stop();
            _logger.LogInformation(
                "✅ Schedule loaded successfully in {ElapsedMs}ms. Stops: {Count}, Size: {SizeKB}KB",
                stopwatch.ElapsedMilliseconds,
                schedule.Stops.Count,
                json.Length / 1024);

            return schedule;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "❌ JSON parse error at {Position}", ex.BytePositionInLine);
            throw new InvalidOperationException(
                $"Failed to parse JSON at position {ex.BytePositionInLine}: {ex.Message}", ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "❌ HTTP request failed. StatusCode: {StatusCode}, Message: {Message}",
                ex.StatusCode, ex.Message);
            throw;
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("⏹️ Schedule load cancelled by user");
            throw;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "⏰ Schedule load timed out after {Timeout}s", _timeout.TotalSeconds);
            throw new TimeoutException($"Schedule load timed out after {_timeout.TotalSeconds}s", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Unexpected error loading schedule");
            throw;
        }
        finally
        {
            stopwatch.Stop();
        }
    }

    /// <summary>
    /// Loads schedule data from a specified URL (overload for testing/custom URLs).
    /// </summary>
    /// <param name="url">The URL to load schedule data from.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>
    /// A <see cref="ScheduleRoot"/> containing the loaded schedule data.
    /// </returns>
    /// <remarks>
    /// This overload allows loading from a custom URL, which is useful for testing
    /// or when the configured BaseAddress needs to be overridden.
    /// If the URL is null or empty, the default configured URL is used.
    /// </remarks>
    public async Task<ScheduleRoot> LoadAsync(string? url, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            return await LoadAsync(cancellationToken);

        var client = _httpClientFactory.CreateClient(HttpClientName);
        client.BaseAddress = new Uri(url);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_timeout);

        try
        {
            var response = await client.GetAsync(url, cts.Token);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cts.Token);
            var schedule = JsonSerializer.Deserialize<ScheduleRoot>(json, _jsonOptions);

            return schedule ?? new ScheduleRoot { Stops = [] };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load schedule from {Url}", url);
            throw;
        }
    }
}