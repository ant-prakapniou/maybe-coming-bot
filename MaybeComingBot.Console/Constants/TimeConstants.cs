namespace MaybeComingBot.Constants;

/// <summary>
/// Contains time-related constant values used throughout the application.
/// </summary>
/// <remarks>
/// Provides centralized time constants for time calculations, validation boundaries,
/// and retry policy configuration. All values are expressed in minutes or derived from minutes.
/// </remarks>
public static class TimeConstants
{
    /// <summary>
    /// Number of minutes in one hour.
    /// </summary>
    /// <value>Constant value: 60.</value>
    public const int MinutesInHour = 60;

    /// <summary>
    /// Number of minutes in one day (24 hours × 60 minutes).
    /// </summary>
    /// <value>Constant value: 1440.</value>
    public const int MinutesInDay = 24 * MinutesInHour; 

    /// <summary>
    /// Maximum valid minutes since midnight representing 23:59.
    /// </summary>
    /// <value>Constant value: 1439 (one minute before midnight).</value>
    /// <remarks>
    /// This value represents the last valid minute of a standard day.
    /// Used for validating user-provided times and ensuring values are within range.
    /// </remarks>
    public const int MaxValidMinutes = MinutesInDay - 1;

    /// <summary>
    /// Maximum minutes since midnight representing 24:00 (end of day).
    /// </summary>
    /// <value>Constant value: 1440.</value>
    /// <remarks>
    /// This is a special value used to denote the end of day for scheduling purposes.
    /// While not a valid time in standard 24-hour format, it's used as a boundary marker
    /// for trips that occur at the end of the service day.
    /// </remarks>
    public const int MaxMinutesInDay = MinutesInDay;
    
    /// <summary>
    /// Base delay in minutes for retry backoff calculations.
    /// </summary>
    /// <value>Constant value: 5 minutes.</value>
    /// <remarks>
    /// Used as the starting point for exponential backoff in Polly retry policies.
    /// The actual delay is calculated as: BaseRetryDelayMinutes × 2^(attempt - 1).
    /// </remarks>
    public const int BaseRetryDelayMinutes = 5;

    /// <summary>
    /// Maximum delay in minutes for retry backoff calculations.
    /// </summary>
    /// <value>Constant value: 240 minutes (4 hours).</value>
    /// <remarks>
    /// Caps the exponential backoff to prevent excessive wait times between retry attempts.
    /// Prevents infinite growth of retry delays in case of prolonged failures.
    /// </remarks>
    public const int MaxRetryDelayMinutes = 240; // 4 hours

    /// <summary>
    /// Maximum number of consecutive failures before giving up on retries.
    /// </summary>
    /// <value>Constant value: 30.</value>
    /// <remarks>
    /// Used as a safety limit in the <see cref="Services.ScheduleRefreshService"/> to prevent
    /// infinite retry loops when the schedule URL is permanently unavailable.
    /// After this many failures, the service will stop attempting to refresh.
    /// </remarks>
    public const int MaxRetryBackoffFailures = 30;
}