namespace MaybeComingBot.Models;

/// <summary>
/// Represents the runtime state of a user interacting with the bot.
/// </summary>
/// <remarks>
/// Stores session data including selected stop, activity tracking, and user information.
/// This state is managed by <see cref="IUserStateService"/> and has a configurable TTL.
/// </remarks>
public sealed class UserState
{
    /// <summary>
    /// Gets or sets the ID of the currently selected stop.
    /// </summary>
    /// <value>
    /// A string containing the stop ID. Empty if no stop is selected.
    /// </value>
    public string SelectedStopId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the timestamp of the user's last activity.
    /// </summary>
    /// <value>
    /// The UTC timestamp of the last interaction. Defaults to <see cref="DateTime.UtcNow"/>.
    /// </value>
    /// <remarks>
    /// Used for TTL expiration checks and determining active users.
    /// Updated on every user interaction.
    /// </remarks>
    public DateTime LastActivity { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the total number of commands executed by this user.
    /// </summary>
    /// <value>
    /// An integer representing the command count. Starts at 0.
    /// </value>
    /// <remarks>
    /// Useful for analytics and understanding user engagement.
    /// Reset by calling <see cref="ResetCommandCount"/>.
    /// </remarks>
    public int CommandCount { get; set; }

    /// <summary>
    /// Gets or sets the user's first name.
    /// </summary>
    /// <value>
    /// A string containing the first name. Empty if not provided.
    /// </value>
    /// <remarks>
    /// Retrieved from Telegram's <see cref="Telegram.Bot.Types.User.FirstName"/>.
    /// </remarks>
    public string FirstName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the user's last name.
    /// </summary>
    /// <value>
    /// A string containing the last name. Empty if not provided.
    /// </value>
    /// <remarks>
    /// Retrieved from Telegram's <see cref="Telegram.Bot.Types.User.LastName"/>.
    /// </remarks>
    public string LastName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the user's Telegram username.
    /// </summary>
    /// <value>
    /// A string containing the username without the @ symbol. Empty if not set.
    /// </value>
    /// <remarks>
    /// Retrieved from Telegram's <see cref="Telegram.Bot.Types.User.Username"/>.
    /// </remarks>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Increments the command count and updates the last activity timestamp.
    /// </summary>
    /// <remarks>
    /// This method should be called on every user interaction to maintain accurate activity tracking.
    /// The <see cref="LastActivity"/> property is updated to the current UTC time.
    /// </remarks>
    public void IncrementCommandCount()
    {
        CommandCount++;
        LastActivity = DateTime.UtcNow;
    }

    /// <summary>
    /// Resets the command count to zero.
    /// </summary>
    /// <remarks>
    /// The <see cref="LastActivity"/> property is not modified by this method.
    /// Useful for analytics resets or when clearing user session data.
    /// </remarks>
    public void ResetCommandCount()
    {
        CommandCount = 0;
    }
}