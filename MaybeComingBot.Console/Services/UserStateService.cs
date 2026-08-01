using MaybeComingBot.Abstractions;
using MaybeComingBot.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace MaybeComingBot.Services;

public class UserStateService : IUserStateService
{
    private readonly ConcurrentDictionary<long, UserState> _states = new();
    private readonly ILogger<UserStateService> _logger;
    private readonly BotSettings _settings;

    public UserStateService(
        IOptions<BotSettings> settings,
        ILogger<UserStateService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public UserState GetOrCreate(long userId)
    {
        return _states.GetOrAdd(userId, _ => new UserState
        {
            SelectedStopId = string.Empty,
            LastActivity = DateTime.UtcNow
        });
    }

    public bool TryGet(long userId, out UserState state)
    {
        return _states.TryGetValue(userId, out state!);
    }

    public void SetStop(long userId, string stopId)
    {
        _states.AddOrUpdate(userId,
            _ => new UserState
            {
                SelectedStopId = stopId,
                LastActivity = DateTime.UtcNow,
                CommandCount = 1
            },
            (_, state) => {
                state.SelectedStopId = stopId;
                state.LastActivity = DateTime.UtcNow;
                state.CommandCount++;
                return state;
            });

        _logger.LogDebug("User {UserId} selected stop {StopId}", userId, stopId);
    }

    public void SetUserInfo(long userId, string firstName, string? lastName, string? username)
    {
        _states.AddOrUpdate(userId,
            _ => new UserState
            {
                FirstName = firstName,
                LastName = lastName ?? string.Empty,
                Username = username ?? string.Empty,
                LastActivity = DateTime.UtcNow
            },
            (_, state) => {
                if (!string.IsNullOrWhiteSpace(firstName)) state.FirstName = firstName;
                if (!string.IsNullOrWhiteSpace(lastName)) state.LastName = lastName;
                if (!string.IsNullOrWhiteSpace(username)) state.Username = username;
                state.LastActivity = DateTime.UtcNow;
                return state;
            });
    }

    public void Clear(long userId)
    {
        if (_states.TryRemove(userId, out _))
            _logger.LogDebug("Cleared state for {UserId}", userId);
    }

    public void UpdateActivity(long userId)
    {
        if (_states.TryGetValue(userId, out var state))
        {
            state.LastActivity = DateTime.UtcNow;
        }
    }

    public void ClearStop(long userId)
    {
        if (_states.TryGetValue(userId, out var state))
        {
            state.SelectedStopId = string.Empty;
            state.LastActivity = DateTime.UtcNow;
            _logger.LogDebug("Cleared stop for user {UserId}", userId);
        }
    }

    public bool HasStopSelected(long userId)
    {
        return _states.TryGetValue(userId, out var state) &&
               !string.IsNullOrEmpty(state.SelectedStopId);
    }

    public int GetActiveUsersCount()
    {
        var ttl = TimeSpan.FromHours(_settings.UserStateTtlHours);
        var now = DateTime.UtcNow;
        return _states.Count(kvp => now - kvp.Value.LastActivity < ttl);
    }

    public IEnumerable<long> GetExpiredKeys(TimeSpan expiry)
    {
        var now = DateTime.UtcNow;
        var keys = _states.Keys.ToList();

        return keys
            .Where(key => _states.TryGetValue(key, out var state) &&
                          now - state.LastActivity > expiry)
            .ToList();
    }
}