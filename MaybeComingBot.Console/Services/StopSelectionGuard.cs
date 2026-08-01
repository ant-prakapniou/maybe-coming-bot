using MaybeComingBot.Abstractions;
using MaybeComingBot.Constants;
using MaybeComingBot.Models;
using MaybeComingBot.Utils;
using Microsoft.Extensions.Logging;

namespace MaybeComingBot.Services;

public class StopSelectionGuard(
    IScheduleStorage storage,
    IUserStateService userStateService,
    TelegramSender sender,
    ILogger<StopSelectionGuard> logger)
{
    private readonly IScheduleStorage _storage = storage;
    private readonly IUserStateService _userStateService = userStateService;
    private readonly TelegramSender _sender = sender;
    private readonly ILogger<StopSelectionGuard> _logger = logger;

    /// <summary>
    /// Ensures that the user has a valid stop selected.
    /// </summary>
    /// <param name="chatId">Telegram chat ID</param>
    /// <param name="userId">Telegram user ID</param>
    /// <returns>Selected stop or null if not selected</returns>
    public async Task<Stop?> EnsureStopSelectedAsync(long chatId, long userId)
    {
        // Проверка валидности ID
        if (chatId <= 0 || userId <= 0)
        {
            _logger.LogWarning("Invalid chatId or userId: Chat={ChatId}, User={UserId}", chatId, userId);
            return null;
        }

        // Проверяем существование состояния
        if (!_userStateService.TryGet(userId, out var state))
        {
            _logger.LogDebug("User {UserId} has no state", userId);
            await _sender.SendAsync(chatId, BotMessages.ChooseStopFirst);
            return null;
        }

        // Проверяем, выбрана ли остановка
        if (string.IsNullOrEmpty(state.SelectedStopId))
        {
            _logger.LogDebug("User {UserId} has no stop selected", userId);
            await _sender.SendAsync(chatId, BotMessages.ChooseStopFirst);
            return null;
        }

        // Проверяем, не истекло ли состояние
        if (_userStateService is UserStateService service && service.IsExpired(userId))
        {
            _logger.LogDebug("User {UserId} state expired", userId);
            _userStateService.Clear(userId);
            await _sender.SendAsync(chatId, BotMessages.SessionExpired);
            return null;
        }

        // Получаем остановку из хранилища
        var stop = _storage.GetStop(state.SelectedStopId);
        if (stop is null)
        {
            // Проверяем, есть ли вообще остановки в хранилище
            var allStops = _storage.GetAllStops();
            if (allStops.Count == 0)
            {
                _logger.LogWarning("No stops available in storage for user {UserId}", userId);
                await _sender.SendAsync(chatId, BotMessages.NoSchedule);
                return null;
            }

            // Остановка действительно не найдена - очищаем состояние
            _userStateService.ClearStop(userId);
            _logger.LogWarning("Stop {StopId} not found for user {UserId}, cleared state",
                state.SelectedStopId, userId);

            await _sender.SendAsync(chatId, BotMessages.StopNotFound);
            return null;
        }

        // Проверяем, есть ли расписание у остановки
        if (stop.Schedule is null || stop.Schedule.Count == 0)
        {
            _logger.LogWarning("Stop {StopId} has no schedule for user {UserId}", stop.Id, userId);
            await _sender.SendAsync(chatId, "⚠️ No schedule available for this stop.");
            return null;
        }

        return stop;
    }

    /// <summary>
    /// Gets the selected stop for a user without sending messages.
    /// </summary>
    /// <param name="userId">Telegram user ID</param>
    /// <returns>Selected stop or null if not selected or invalid</returns>
    public Stop? GetSelectedStop(long userId)
    {
        if (userId <= 0) return null;

        if (!_userStateService.TryGet(userId, out var state) ||
            string.IsNullOrEmpty(state?.SelectedStopId))
        {
            return null;
        }

        var stop = _storage.GetStop(state.SelectedStopId);
        if (stop is null)
        {
            // Очищаем некорректное состояние без уведомления
            _userStateService.ClearStop(userId);
            _logger.LogDebug("Cleared invalid stop {StopId} for user {UserId}",
                state.SelectedStopId, userId);
            return null;
        }

        return stop;
    }

    /// <summary>
    /// Checks if user has a valid stop selected.
    /// </summary>
    /// <param name="userId">Telegram user ID</param>
    /// <returns>True if user has a valid stop selected</returns>
    public bool HasValidStop(long userId)
    {
        if (userId <= 0) return false;

        if (!_userStateService.TryGet(userId, out var state) ||
            string.IsNullOrEmpty(state?.SelectedStopId))
        {
            return false;
        }

        var stop = _storage.GetStop(state.SelectedStopId);
        if (stop is null)
        {
            // Очищаем некорректное состояние
            _userStateService.ClearStop(userId);
            return false;
        }

        // Проверяем наличие расписания
        return stop.Schedule is not null && stop.Schedule.Count > 0;
    }
}