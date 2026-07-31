using MaybeComingBot.Models;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;

namespace MaybeComingBot.Services;

public class ScheduleLoader
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ScheduleLoader> _logger;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public ScheduleLoader(
        IHttpClientFactory httpClientFactory,
        ILogger<ScheduleLoader> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<ScheduleRoot> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(nameof(ScheduleLoader));

            var schedule = await client.GetFromJsonAsync<ScheduleRoot>(
                string.Empty, // URL уже задан в HttpClient при регистрации
                _jsonOptions,
                cancellationToken);

            if (schedule is null || schedule.Stops is null)
            {
                _logger.LogWarning("Schedule response is null or empty");
                return new ScheduleRoot { Stops = [] };
            }

            _logger.LogDebug("Loaded {Count} stops", schedule.Stops.Count);
            return schedule;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON parse error");
            throw new InvalidOperationException($"Failed to parse JSON: {ex.Message}", ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP request failed");
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Schedule load cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Load error");
            throw;
        }
    }
}