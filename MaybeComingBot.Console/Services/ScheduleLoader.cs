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

    public async Task<ScheduleRoot> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var schedule = await _httpClient.GetFromJsonAsync<ScheduleRoot>(
                _jsonUrl,
                _jsonOptions,
                cancellationToken);

            if (schedule is null || schedule.Stops is null)
                return new ScheduleRoot { Stops = [] };

            return schedule;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON parse error");
            throw new InvalidOperationException($"Failed to parse JSON: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Load error");
            throw;
        }
    }
}