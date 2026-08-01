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

    public async Task<Stop?> EnsureStopSelectedAsync(long chatId, long userId)
    {
        // Проверяем, есть ли у пользователя выбранная остановка
        if (!_userStateService.TryGet(userId, out var state) ||
            string.IsNullOrEmpty(state?.SelectedStopId))
        {
            await _sender.SendAsync(chatId, BotMessages.ChooseStopFirst);
            return null;
        }

        // Получаем остановку из хранилища
        var stop = _storage.GetStop(state.SelectedStopId);
        if (stop is null)
        {
            // Очищаем некорректное состояние
            _userStateService.ClearStop(userId);
            _logger.LogWarning("Stop {StopId} not found for user {UserId}, cleared state",
                state.SelectedStopId, userId);

            await _sender.SendAsync(chatId, BotMessages.StopNotFound);
            return null;
        }

        return stop;
    }
}