using MaybeComingBot.Abstractions;
using MaybeComingBot.Constants;
using MaybeComingBot.Models;
using Microsoft.Extensions.Logging;

namespace MaybeComingBot.Services;

/// <summary>
/// Ensures that users have a valid stop selected before executing commands.
/// </summary>
/// <remarks>
/// This guard class validates the user's session state and selected stop,
/// providing friendly error messages when validation fails. It handles:
/// <list type="bullet">
/// <item><description>Missing or expired user sessions</description></item>
/// <item><description>No stop selected</description></item>
/// <item><description>Invalid or missing stops in the schedule</description></item>
/// <item><description>Stops with no schedule data</description></item>
/// </list>
/// </remarks>
public class StopSelectionGuard(
    IScheduleStorage storage,
    IUserStateService userStateService,
    ITelegramSender sender,
    ILogger<StopSelectionGuard> logger)
{
    private readonly IScheduleStorage _storage = storage;
    private readonly IUserStateService _userStateService = userStateService;
    private readonly ITelegramSender _sender = sender;
    private readonly ILogger<StopSelectionGuard> _logger = logger;

    /// <summary>
    /// Ensures that the user has a valid stop selected.
    /// </summary>
    /// <param name="chatId">Telegram chat ID for sending messages.</param>
    /// <param name="userId">Telegram user ID to check.</param>
    /// <returns>
    /// The selected <see cref="Stop"/> if valid; otherwise, <c>null</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The validation flow:
    /// <list type="number">
    /// <item><description>Validates chat and user IDs</description></item>
    /// <item><description>Checks if user has a session state</description></item>
    /// <item><description>Checks if a stop is selected</description></item>
    /// <item><description>Retrieves the stop from storage</description></item>
    /// <item><description>Verifies the stop has schedule data</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// If any validation fails, an appropriate user message is sent
    /// and the method returns <c>null</c>.
    /// </para>
    /// </remarks>
    public async Task<Stop?> EnsureStopSelectedAsync(long chatId, long userId)
    {
        if (chatId <= 0 || userId <= 0)
        {
            _logger.LogWarning("Invalid chatId or userId: Chat={ChatId}, User={UserId}", chatId, userId);
            return null;
        }

        if (!_userStateService.TryGet(userId, out var state))
        {
            _logger.LogDebug("User {UserId} has no state", userId);
            await _sender.SendAsync(chatId, BotMessages.ChooseStopFirst);
            return null;
        }

        if (string.IsNullOrEmpty(state.SelectedStopId))
        {
            _logger.LogDebug("User {UserId} has no stop selected", userId);
            await _sender.SendAsync(chatId, BotMessages.ChooseStopFirst);
            return null;
        }

        var stop = _storage.GetStop(state.SelectedStopId);
        if (stop is null)
        {
            var allStops = _storage.GetAllStops();
            if (allStops.Count is 0)
            {
                _logger.LogWarning("No stops available in storage for user {UserId}", userId);
                await _sender.SendAsync(chatId, BotMessages.NoSchedule);
                return null;
            }

            _userStateService.ClearStop(userId);
            _logger.LogWarning("Stop {StopId} not found for user {UserId}, cleared state",
                state.SelectedStopId, userId);

            await _sender.SendAsync(chatId, BotMessages.StopNotFound);
            return null;
        }

        if (stop.Schedule is null || stop.Schedule.Count is 0)
        {
            _logger.LogWarning("Stop {StopId} has no schedule for user {UserId}", stop.Id, userId);
            await _sender.SendAsync(chatId, BotMessages.StopHasNoSchedule);
            return null;
        }

        return stop;
    }
}
