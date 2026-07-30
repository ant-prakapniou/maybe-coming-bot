using MaybeComingBot.Abstractions;
using MaybeComingBot.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace MaybeComingBot.Services;

public class UserStateService : IUserStateService
{
    private readonly ConcurrentDictionary<long, UserState> _states = new();
    private readonly ILogger<UserStateService> _logger;

    public UserStateService(ILogger<UserStateService> logger)
    {
        _logger = logger;
    }

    public UserState GetOrCreate(long userId)
    {
        return _states.GetOrAdd(userId, _ => new UserState
        {
            LastActivity = DateTime.UtcNow
        });
    }

    public bool TryGet(long userId, out UserState state)
    {
        return _states.TryGetValue(userId, out state!);
    }

    public void SetStop(long userId, string stopId)
    {
        var state = GetOrCreate(userId);
        state.SelectedStopId = stopId;
        state.LastActivity = DateTime.UtcNow;
        _logger.LogDebug("User {UserId} selected stop {StopId}", userId, stopId);
    }

    public void SetUserInfo(long userId, string firstName, string? lastName, string? username)
    {
        var state = GetOrCreate(userId);
        state.FirstName = firstName ?? string.Empty;
        state.LastName = lastName ?? string.Empty;
        state.Username = username ?? string.Empty;
        state.LastActivity = DateTime.UtcNow;
    }

    public void Clear(long userId)
    {
        if (_states.TryRemove(userId, out _))
        {
            _logger.LogDebug("Cleared state for user {UserId}", userId);
        }
    }

    public void UpdateActivity(long userId)
    {
        if (_states.TryGetValue(userId, out var state))
        {
            state.LastActivity = DateTime.UtcNow;
        }
    }

    public IEnumerable<long> GetExpiredKeys(TimeSpan expiry)
    {
        var now = DateTime.UtcNow;
        return _states
            .Where(kvp => now - kvp.Value.LastActivity > expiry)
            .Select(kvp => kvp.Key)
            .ToList();
    }

    public bool HasStopSelected(long userId)
    {
        return _states.TryGetValue(userId, out var state) &&
               !string.IsNullOrEmpty(state.SelectedStopId);
    }

    public int GetActiveUsersCount()
    {
        var now = DateTime.UtcNow;
        return _states.Count(kvp => now - kvp.Value.LastActivity < TimeSpan.FromHours(1));
    }
}