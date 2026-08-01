namespace MaybeComingBot.Abstractions;

/// <summary>
/// Defines the contract for the schedule refresh service.
/// </summary>
/// <remarks>
/// This service is responsible for loading and refreshing schedule data from the configured URL.
/// It runs as a background service and provides status information for monitoring.
/// </remarks>
public interface IScheduleRefreshService
{
    /// <summary>
    /// Forces an immediate refresh of the schedule data.
    /// </summary>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// This method bypasses the normal refresh interval and performs an immediate load.
    /// Useful for admin-initiated updates or recovery from failed loads.
    /// </remarks>
    Task ForceRefreshAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets a human-readable status string describing the current state of the schedule.
    /// </summary>
    /// <returns>
    /// A string containing the status information.
    /// Possible values: "NOT LOADED", "✅ LAST: HH:mm:ss (Xh Ym ago)", or "⚠️ DATA STALE: ...".
    /// </returns>
    /// <remarks>
    /// The status includes the last successful update time and indicates if the data is stale.
    /// Staleness is determined based on the configured <see cref="Models.BotSettings.UpdateIntervalHours"/>.
    /// </remarks>
    string GetStatus();

    /// <summary>
    /// Gets information about the last refresh attempt.
    /// </summary>
    /// <returns>
    /// A string containing the timestamp of the last attempt and the current failure count.
    /// Example: "ATTEMPT: HH:mm:ss (Xh Ym ago), FAILS: 3".
    /// </returns>
    /// <remarks>
    /// Useful for diagnosing refresh issues and monitoring service health.
    /// The failure count is reset to 0 on successful refresh.
    /// </remarks>
    string GetLastAttemptInfo();

    /// <summary>
    /// Gets the timestamp of the last successful schedule update.
    /// </summary>
    /// <value>
    /// The UTC timestamp of the last successful refresh, or <see cref="DateTime.MinValue"/> if never updated.
    /// </value>
    /// <remarks>
    /// Used for health checks and monitoring to determine if the schedule data is current.
    /// </remarks>
    DateTime LastSuccessfulUpdate { get; }

    /// <summary>
    /// Gets the current number of consecutive refresh failures.
    /// </summary>
    /// <value>
    /// An integer representing the number of failed refresh attempts since the last success.
    /// </value>
    /// <remarks>
    /// Used for exponential backoff calculations and monitoring.
    /// Reset to 0 when a refresh succeeds.
    /// </remarks>
    int FailureCount { get; }
}