using MaybeComingBot.Models;

namespace MaybeComingBot.Abstractions;

/// <summary>
/// Defines the contract for managing user session state.
/// </summary>
/// <remarks>
/// This service manages in-memory user state including selected stops, activity tracking,
/// and user information. States have a configurable time-to-live (TTL) and are
/// automatically cleaned up by <see cref="Services.UserStateCleanupService"/>.
/// All operations are thread-safe.
/// </remarks>
public interface IUserStateService
{
    /// <summary>
    /// Retrieves an existing user state or creates a new one.
    /// </summary>
    /// <param name="userId">The Telegram user ID. Must be greater than 0.</param>
    /// <returns>
    /// The <see cref="UserState"/> for the specified user, never null.
    /// </returns>
    UserState GetOrCreate(long userId);

    /// <summary>
    /// Attempts to retrieve a user's state.
    /// </summary>
    /// <param name="userId">The Telegram user ID. Must be greater than 0.</param>
    /// <param name="state">When this method returns, contains the user state if found; otherwise, null.</param>
    /// <returns>
    /// <c>true</c> if the user state exists; otherwise, <c>false</c>.
    /// </returns>
    bool TryGet(long userId, out UserState state);

    /// <summary>
    /// Sets or updates the selected stop for a user.
    /// </summary>
    /// <param name="userId">The Telegram user ID. Must be greater than 0.</param>
    /// <param name="stopId">The stop identifier. Cannot be null or empty.</param>
    void SetStop(long userId, string stopId);

    /// <summary>
    /// Sets or updates user information (name and username).
    /// </summary>
    /// <param name="userId">The Telegram user ID. Must be greater than 0.</param>
    /// <param name="firstName">The user's first name. Cannot be null.</param>
    /// <param name="lastName">The user's last name. Can be null.</param>
    /// <param name="username">The user's Telegram username without @. Can be null.</param>
    /// <remarks>
    /// This method updates the state if it exists, or creates a new one if not.
    /// Existing state data (e.g., selected stop) is preserved.
    /// </remarks>
    void SetUserInfo(long userId, string firstName, string? lastName, string? username);

    /// <summary>
    /// Completely removes a user's state.
    /// </summary>
    /// <param name="userId">The Telegram user ID. Must be greater than 0.</param>
    /// <remarks>
    /// All user data including selected stop and command context is permanently deleted.
    /// This is typically called when a user's session expires or when using the /cancel command.
    /// </remarks>
    void Clear(long userId);

    /// <summary>
    /// Updates the user's last activity timestamp.
    /// </summary>
    /// <param name="userId">The Telegram user ID. Must be greater than 0.</param>
    /// <remarks>
    /// This method creates a new state if one doesn't exist.
    /// The updated timestamp is used for TTL expiration checks.
    /// </remarks>
    void UpdateActivity(long userId);

    /// <summary>
    /// Gets a collection of user IDs whose states have expired.
    /// </summary>
    /// <param name="expiry">The time-to-live duration for user states.</param>
    /// <returns>
    /// An enumerable collection of expired user IDs.
    /// </returns>
    /// <remarks>
    /// Used by <see cref="Services.UserStateCleanupService"/> to identify and remove
    /// inactive user states.
    /// </remarks>
    IEnumerable<long> GetExpiredKeys(TimeSpan expiry);

    /// <summary>
    /// Determines whether a user has a stop selected.
    /// </summary>
    /// <param name="userId">The Telegram user ID. Must be greater than 0.</param>
    /// <returns>
    /// <c>true</c> if the user has a valid selected stop; otherwise, <c>false</c>.
    /// </returns>
    bool HasStopSelected(long userId);

    /// <summary>
    /// Gets the number of active users (states not expired).
    /// </summary>
    /// <returns>
    /// The count of active user states.
    /// </returns>
    /// <remarks>
    /// Uses the configured TTL from <see cref="Models.BotSettings.UserStateTtlHours"/>
    /// to determine which states are considered active.
    /// </remarks>
    int GetActiveUsersCount();

    /// <summary>
    /// Clears the selected stop for a user without removing the entire state.
    /// </summary>
    /// <param name="userId">The Telegram user ID. Must be greater than 0.</param>
    /// <remarks>
    /// Unlike <see cref="Clear"/>, this method preserves user information (name, username)
    /// and command history while clearing only the stop selection.
    /// </remarks>
    void ClearStop(long userId);

    /// <summary>
    /// Checks whether a user's state has expired based on the configured TTL.
    /// </summary>
    /// <param name="userId">The Telegram user ID. Must be greater than 0.</param>
    /// <returns>
    /// <c>true</c> if the user state has expired or doesn't exist; otherwise, <c>false</c>.
    /// </returns>
    bool IsExpired(long userId);
}