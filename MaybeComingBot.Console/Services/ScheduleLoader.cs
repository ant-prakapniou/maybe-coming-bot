using MaybeComingBot.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text.Json;

namespace MaybeComingBot.Services;

public class ScheduleLoader(
    HttpClient httpClient,
    IOptions<BotSettings> settings,
    ILogger<ScheduleLoader> logger)
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly string _jsonUrl = settings.Value.JsonUrl;
    private readonly ILogger<ScheduleLoader> _logger = logger;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public string JsonUrl => _jsonUrl;

    public async Task<ScheduleRoot> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Loading schedule from {JsonUrl}", _jsonUrl);

            var schedule = await _httpClient.GetFromJsonAsync<ScheduleRoot>(
                _jsonUrl,
                _jsonOptions,
                cancellationToken);

            if (schedule is null)
            {
                _logger.LogWarning("Schedule deserialized to null, returning empty schedule");
                return new ScheduleRoot { Stops = [] };
            }

            if (schedule.Stops is null)
            {
                _logger.LogWarning("Schedule.Stops is null, initializing empty list");
                schedule.Stops = [];
            }

            _logger.LogInformation("Schedule loaded successfully. Stops: {StopCount}", schedule.Stops.Count);
            return schedule;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON deserialization error loading schedule from {JsonUrl}", _jsonUrl);
            throw new InvalidOperationException($"Failed to parse JSON: {ex.Message}", ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error loading schedule from {JsonUrl}", _jsonUrl);
            throw;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "Timeout loading schedule from {JsonUrl}", _jsonUrl);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error loading schedule from {JsonUrl}", _jsonUrl);
            throw;
        }
    }
}