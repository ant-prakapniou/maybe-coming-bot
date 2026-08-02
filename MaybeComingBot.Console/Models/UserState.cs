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

}
