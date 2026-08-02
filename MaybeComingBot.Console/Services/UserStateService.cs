using MaybeComingBot.Abstractions;
using MaybeComingBot.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace MaybeComingBot.Services;

/// <summary>
/// Manages in-memory user session states with thread-safe operations.
/// </summary>
/// <remarks>
/// This service provides:
/// <list type="bullet">
/// <item><description>Thread-safe storage using <see cref="ConcurrentDictionary{TKey,TValue}"/></description></item>
/// <item><description>User state with TTL (Time-To-Live) expiration</description></item>
/// <item><description>Selected stop tracking per user</description></item>
/// <item><description>User information storage (name, username)</description></item>
/// <item><description>Activity tracking for expiration calculations</description></item>
/// </list>
/// </remarks>
/// <param name="settings">The bot settings containing TTL configuration.</param>
/// <param name="logger">The logger instance for this class.</param>
public class UserStateService(
    IOptions<BotSettings> settings,
    ILogger<UserStateService> logger) : IUserStateService
{
    private readonly ConcurrentDictionary<long, UserState> _states = new();
    private readonly ILogger<UserStateService> _logger = logger;
    private readonly BotSettings _settings = settings.Value;
    private const int DefaultTtlHours = 24;

    /// <inheritdoc/>
    public UserState GetOrCreate(long userId)
    {
        if (userId <= 0)
            throw new ArgumentException("Invalid userId", nameof(userId));

        return _states.GetOrAdd(userId, _ => CreateUserState());
    }

    /// <inheritdoc/>
    public bool TryGet(long userId, out UserState state)
    {
        if (userId <= 0)
        {
            state = null!;
            return false;
        }

        return _states.TryGetValue(userId, out state!);
    }

    /// <inheritdoc/>
    public void SetStop(long userId, string stopId)
    {
        if (userId <= 0)
            throw new ArgumentException("Invalid userId", nameof(userId));

        if (string.IsNullOrWhiteSpace(stopId))
            throw new ArgumentException("Invalid stopId", nameof(stopId));

        _states.AddOrUpdate(userId,
            _ => CreateUserState(stopId: stopId),
            (_, state) => {
                state.SelectedStopId = stopId;
                state.LastActivity = DateTime.UtcNow;
                return state;
            });

        _logger.LogDebug("User {UserId} selected stop {StopId}", userId, stopId);
    }

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public void Clear(long userId)
    {
        if (userId <= 0) return;

        if (_states.TryRemove(userId, out _))
            _logger.LogDebug("Cleared state for {UserId}", userId);
    }

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public bool HasStopSelected(long userId)
    {
        if (userId <= 0) return false;

        return _states.TryGetValue(userId, out var state) &&
               !string.IsNullOrEmpty(state.SelectedStopId);
    }

    /// <inheritdoc/>
    public int GetActiveUsersCount()
    {
        var ttl = TimeSpan.FromHours(_settings.UserStateTtlHours > 0
            ? _settings.UserStateTtlHours
            : DefaultTtlHours);
        var now = DateTime.UtcNow;

        return _states.Values.Count(state => now - state.LastActivity < ttl);
    }

    /// <inheritdoc/>
    public IEnumerable<long> GetExpiredKeys(TimeSpan expiry)
    {
        var now = DateTime.UtcNow;

        return _states
            .Where(kvp => now - kvp.Value.LastActivity > expiry)
            .Select(kvp => kvp.Key)
            .ToList();
    }

    /// <inheritdoc/>
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

    /// <summary>
    /// Creates a new user state with optional initial values.
    /// </summary>
    /// <param name="firstName">The user's first name.</param>
    /// <param name="lastName">The user's last name.</param>
    /// <param name="username">The user's Telegram username.</param>
    /// <param name="stopId">The selected stop ID.</param>
    /// <returns>A new <see cref="UserState"/> instance.</returns>
    private static UserState CreateUserState(
        string? firstName = null,
        string? lastName = null,
        string? username = null,
        string? stopId = null)
    {
        return new UserState
        {
            FirstName = firstName ?? string.Empty,
            LastName = lastName ?? string.Empty,
            Username = username ?? string.Empty,
            SelectedStopId = stopId ?? string.Empty,
            LastActivity = DateTime.UtcNow
        };
    }
}