using System.Net.Http.Json;
using MaybeComingBot.Console.Models;
using Microsoft.Extensions.Configuration;

namespace MaybeComingBot.Console.Services;

/// <summary>
/// Сервис для загрузки расписания из JSON-файла по ссылке.
/// </summary>
public class ScheduleLoader
{
    private readonly HttpClient _httpClient;
    private readonly string _jsonUrl;

    public ScheduleLoader(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;

        // Читаем JsonUrl из секции "Bot"
        _jsonUrl = configuration["Bot:JsonUrl"]
            ?? throw new InvalidOperationException("JsonUrl is not configured in Bot section.");
    }

    /// <summary>
    /// Загружает и десериализует расписание из удалённого JSON-файла.
    /// </summary>
    public async Task<ScheduleRoot> LoadAsync()
    {
        try
        {
            var schedule = await _httpClient.GetFromJsonAsync<ScheduleRoot>(_jsonUrl);
            return schedule ?? new ScheduleRoot { Stops = new List<Stop>() };
        }
        catch (Exception ex)
        {
            // Логируем ошибку
            System.Console.WriteLine($"[ERROR] Failed to load schedule: {ex.Message}");
            throw;
        }
    }
}