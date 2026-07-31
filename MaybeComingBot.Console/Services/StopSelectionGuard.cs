using MaybeComingBot.Abstractions;
using MaybeComingBot.Constants;
using MaybeComingBot.Models;
using MaybeComingBot.Utils;

namespace MaybeComingBot.Services;

public class StopSelectionGuard(
    IScheduleStorage storage,
    IUserStateService userStateService,
    TelegramSender sender)
{
    private readonly IScheduleStorage _storage = storage;
    private readonly IUserStateService _userStateService = userStateService;
    private readonly TelegramSender _sender = sender;

    public async Task<Stop?> EnsureStopSelectedAsync(long chatId, long userId)
    {
        if (!_userStateService.HasStopSelected(userId))
        {
            await _sender.SendAsync(chatId, BotMessages.ChooseStopFirst);
            return null;
        }

        if (!_userStateService.TryGet(userId, out var state) ||
            string.IsNullOrEmpty(state.SelectedStopId))
        {
            await _sender.SendAsync(chatId, BotMessages.ChooseStopFirst);
            return null;
        }

        var stop = _storage.GetStop(state.SelectedStopId);
        if (stop is null)
        {
            await _sender.SendAsync(chatId, BotMessages.StopNotFound);
            return null;
        }

        return stop;
    }
}