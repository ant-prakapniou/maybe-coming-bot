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
    private const int DefaultTtlHours = 24;

    public UserStateService(
        IOptions<BotSettings> settings,
        ILogger<UserStateService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public UserState GetOrCreate(long userId)
    {
        if (userId <= 0)
            throw new ArgumentException("Invalid userId", nameof(userId));

        return _states.GetOrAdd(userId, _ => CreateUserState());
    }

    public bool TryGet(long userId, out UserState state)
    {
        if (userId <= 0)
        {
            state = null!;
            return false;
        }

        return _states.TryGetValue(userId, out state!);
    }

    public void SetStop(long userId, string stopId)
    {
        if (userId <= 0)
            throw new ArgumentException("Invalid userId", nameof(userId));

        if (string.IsNullOrWhiteSpace(stopId))
            throw new ArgumentException("Invalid stopId", nameof(stopId));

        _states.AddOrUpdate(userId,
            _ => CreateUserState(stopId: stopId, commandCount: 1),
            (_, state) => {
                state.SelectedStopId = stopId;
                state.LastActivity = DateTime.UtcNow;
                return state;
            });

        _logger.LogDebug("User {UserId} selected stop {StopId}", userId, stopId);
    }

    public void SetUserInfo(long userId, string firstName, string? lastName, string? username)
    {
        if (userId <= 0)
            throw new ArgumentException("Invalid userId", nameof(userId));

        _states.AddOrUpdate(userId,
            _ => CreateUserState(firstName, lastName, username),
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
        if (userId <= 0) return;

        if (_states.TryRemove(userId, out _))
            _logger.LogDebug("Cleared state for {UserId}", userId);
    }

    public void UpdateActivity(long userId)
    {
        if (userId <= 0) return;

        _states.AddOrUpdate(userId,
            _ => CreateUserState(),
            (_, state) => {
                state.LastActivity = DateTime.UtcNow;
                return state;
            });
    }

    public void ClearStop(long userId)
    {
        if (userId <= 0) return;

        if (_states.TryGetValue(userId, out var state))
        {
            state.SelectedStopId = string.Empty;
            state.LastActivity = DateTime.UtcNow;
            _logger.LogDebug("Cleared stop for user {UserId}", userId);
        }
    }

    public bool HasStopSelected(long userId)
    {
        if (userId <= 0) return false;

        return _states.TryGetValue(userId, out var state) &&
               !string.IsNullOrEmpty(state.SelectedStopId);
    }

    public int GetActiveUsersCount()
    {
        var ttl = TimeSpan.FromHours(_settings.UserStateTtlHours > 0
            ? _settings.UserStateTtlHours
            : DefaultTtlHours);
        var now = DateTime.UtcNow;

        return _states.Values.Count(state => now - state.LastActivity < ttl);
    }

    public IEnumerable<long> GetExpiredKeys(TimeSpan expiry)
    {
        var now = DateTime.UtcNow;

        return _states
            .Where(kvp => now - kvp.Value.LastActivity > expiry)
            .Select(kvp => kvp.Key)
            .ToList();
    }

    public bool IsExpired(long userId)
    {
        if (userId <= 0) return true;

        if (!_states.TryGetValue(userId, out var state))
            return true;

        var ttl = TimeSpan.FromHours(_settings.UserStateTtlHours > 0
            ? _settings.UserStateTtlHours
            : DefaultTtlHours);

        return DateTime.UtcNow - state.LastActivity > ttl;
    }

    private static UserState CreateUserState(
        string? firstName = null,
        string? lastName = null,
        string? username = null,
        string? stopId = null,
        int commandCount = 0)
    {
        return new UserState
        {
            FirstName = firstName ?? string.Empty,
            LastName = lastName ?? string.Empty,
            Username = username ?? string.Empty,
            SelectedStopId = stopId ?? string.Empty,
            LastActivity = DateTime.UtcNow,
            CommandCount = commandCount
        };
    }
}