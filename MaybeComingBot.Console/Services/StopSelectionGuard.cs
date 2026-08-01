using MaybeComingBot.Abstractions;
using MaybeComingBot.Constants;
using MaybeComingBot.Models;
using MaybeComingBot.Utils;
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
    /// <item><description>Verifies the state hasn't expired</description></item>
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
        // Validate IDs
        if (chatId <= 0 || userId <= 0)
        {
            _logger.LogWarning("Invalid chatId or userId: Chat={ChatId}, User={UserId}", chatId, userId);
            return null;
        }

        // Check for existing state
        if (!_userStateService.TryGet(userId, out var state))
        {
            _logger.LogDebug("User {UserId} has no state", userId);
            await _sender.SendAsync(chatId, BotMessages.ChooseStopFirst);
            return null;
        }

        // Check if a stop is selected
        if (string.IsNullOrEmpty(state.SelectedStopId))
        {
            _logger.LogDebug("User {UserId} has no stop selected", userId);
            await _sender.SendAsync(chatId, BotMessages.ChooseStopFirst);
            return null;
        }

        // Check if state has expired
        if (_userStateService is UserStateService service && service.IsExpired(userId))
        {
            _logger.LogDebug("User {UserId} state expired", userId);
            _userStateService.Clear(userId);
            await _sender.SendAsync(chatId, BotMessages.SessionExpired);
            return null;
        }

        // Retrieve the stop from storage
        var stop = _storage.GetStop(state.SelectedStopId);
        if (stop is null)
        {
            // Check if there are any stops available
            var allStops = _storage.GetAllStops();
            if (allStops.Count == 0)
            {
                _logger.LogWarning("No stops available in storage for user {UserId}", userId);
                await _sender.SendAsync(chatId, BotMessages.NoSchedule);
                return null;
            }

            // Stop not found - clear the invalid state
            _userStateService.ClearStop(userId);
            _logger.LogWarning("Stop {StopId} not found for user {UserId}, cleared state",
                state.SelectedStopId, userId);

            await _sender.SendAsync(chatId, BotMessages.StopNotFound);
            return null;
        }

        // Check if the stop has schedule data
        if (stop.Schedule is null || stop.Schedule.Count == 0)
        {
            _logger.LogWarning("Stop {StopId} has no schedule for user {UserId}", stop.Id, userId);
            await _sender.SendAsync(chatId, "⚠️ No schedule available for this stop.");
            return null;
        }

        return stop;
    }

    /// <summary>
    /// Gets the selected stop for a user without sending any messages.
    /// </summary>
    /// <param name="userId">Telegram user ID to check.</param>
    /// <returns>
    /// The selected <see cref="Stop"/> if valid; otherwise, <c>null</c>.
    /// </returns>
    /// <remarks>
    /// This method silently validates the user's state without sending any
    /// user-facing messages. Invalid states are cleared automatically.
    /// Useful for background checks or API calls where user feedback
    /// is not appropriate.
    /// </remarks>
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
            // Clear invalid state silently
            _userStateService.ClearStop(userId);
            _logger.LogDebug("Cleared invalid stop {StopId} for user {UserId}",
                state.SelectedStopId, userId);
            return null;
        }

        return stop;
    }

    /// <summary>
    /// Checks if a user has a valid stop selected.
    /// </summary>
    /// <param name="userId">Telegram user ID to check.</param>
    /// <returns>
    /// <c>true</c> if the user has a valid stop selected; otherwise, <c>false</c>.
    /// </returns>
    /// <remarks>
    /// This is a lightweight check that returns a boolean without sending
    /// any messages or throwing exceptions. Invalid states are cleared
    /// automatically. Useful for conditional logic.
    /// </remarks>
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
            // Clear invalid state silently
            _userStateService.ClearStop(userId);
            return false;
        }

        // Check for schedule data
        return stop.Schedule is not null && stop.Schedule.Count > 0;
    }
}