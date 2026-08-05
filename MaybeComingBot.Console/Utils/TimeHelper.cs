using MaybeComingBot.Constants;
using System.Collections.Concurrent;
using System.Text;

namespace MaybeComingBot.Utils;

/// <summary>
/// Provides helper methods for time-related operations including parsing,
/// formatting, validation, and Markdown escaping.
/// </summary>
/// <remarks>
/// This class centralizes all time manipulation logic used throughout the application.
/// It includes caching for parsed time values to improve performance.
/// </remarks>
public static class TimeHelper
{
    private static readonly HashSet<char> MarkdownSpecialChars = new()
    {
        '_', '*', '[', ']', '(', ')', '~', '`', '>', '#', '+', '-', '=', '|', '{', '}', '.', '!', '\\', '/'
    };

    private static readonly ConcurrentDictionary<string, int?> _cache = new();

    private const int DefaultMinutes = 0;
    private const int MinutesInHour = 60;
    private const int MinutesInDay = 1440;

    /// <summary>
    /// Converts a time string (HH:mm) to minutes since midnight.
    /// </summary>
    /// <param name="time">Time in HH:mm format. Can be "H:mm" or "24:00" for end of day.</param>
    /// <returns>Minutes since midnight, or null if the format is invalid.</returns>
    /// <remarks>
    /// The method handles the following formats:
    /// <list type="bullet">
    /// <item><description>"HH:mm" — standard 24-hour format (e.g., "14:30")</description></item>
    /// <item><description>"H:mm" — hour without leading zero (e.g., "9:00")</description></item>
    /// <item><description>"24:00" — special case representing the end of the day (1440 minutes)</description></item>
    /// <item><description>"HH:mm:ss" — with seconds (seconds are ignored)</description></item>
    /// </list>
    /// Results are cached for performance using a concurrent dictionary.
    /// </remarks>
    public static int? ToMinutes(string time)
    {
        if (string.IsNullOrWhiteSpace(time))
            return null;

        return _cache.GetOrAdd(time, t =>
        {
            var parts = t.Split(':', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || parts.Length > 3)
                return null;

            if (!int.TryParse(parts[0], out int hours))
                return null;

            if (!int.TryParse(parts[1], out int minutes))
                return null;

            // Special case: 24:00 represents the end of the day
            if (hours == 24 && minutes == 0)
                return TimeConstants.MaxMinutesInDay;

            // Validate time range
            if (hours >= 0 && hours <= 23 && minutes >= 0 && minutes <= 59)
                return hours * TimeConstants.MinutesInHour + minutes;

            return null;
        });
    }

    /// <summary>
    /// Converts a time string to minutes since midnight with a default fallback value.
    /// </summary>
    /// <param name="time">Time in HH:mm format.</param>
    /// <param name="defaultValue">Default value to return if parsing fails.</param>
    /// <returns>Minutes since midnight, or <paramref name="defaultValue"/> if invalid.</returns>
    public static int ToMinutesSafe(string time, int defaultValue = DefaultMinutes)
    {
        return ToMinutes(time) ?? defaultValue;
    }

    /// <summary>
    /// Attempts to parse a time string to minutes since midnight.
    /// </summary>
    /// <param name="time">Time in HH:mm format.</param>
    /// <param name="minutes">When this method returns, contains the parsed minutes if successful; otherwise, 0.</param>
    /// <returns><c>true</c> if parsing succeeded; otherwise, <c>false</c>.</returns>
    public static bool TryParseMinutes(string time, out int minutes)
    {
        var result = ToMinutes(time);
        if (result.HasValue)
        {
            minutes = result.Value;
            return true;
        }
        minutes = DefaultMinutes;
        return false;
    }

    /// <summary>
    /// Checks whether a time string is valid.
    /// </summary>
    /// <param name="time">Time in HH:mm format.</param>
    /// <returns><c>true</c> if the time format is valid; otherwise, <c>false</c>.</returns>
    public static bool IsValidTime(string time)
    {
        return ToMinutes(time).HasValue;
    }

    /// <summary>
    /// Formats minutes since midnight to a time string (HH:mm).
    /// </summary>
    /// <param name="minutes">Minutes since midnight (0-1440).</param>
    /// <returns>Formatted time string (e.g., "14:30" or "24:00").</returns>
    public static string FormatMinutes(int minutes)
    {
        if (minutes >= TimeConstants.MaxMinutesInDay)
            return "24:00";

        var hours = minutes / TimeConstants.MinutesInHour;
        var mins = minutes % TimeConstants.MinutesInHour;
        return $"{hours:D2}:{mins:D2}";
    }

    /// <summary>
    /// Formats a time span in minutes to a human-readable string.
    /// </summary>
    /// <param name="totalMinutes">Total minutes to format.</param>
    /// <returns>
    /// A human-readable string like "2d 3h 15m", "NOW" for 0 or negative values.
    /// </returns>
    public static string FormatTimeSpan(int totalMinutes)
    {
        if (totalMinutes <= 0)
            return "NOW";

        var days = totalMinutes / TimeConstants.MinutesInDay;
        var remaining = totalMinutes % TimeConstants.MinutesInDay;
        var hours = remaining / TimeConstants.MinutesInHour;
        var minutes = remaining % TimeConstants.MinutesInHour;

        var parts = new List<string>();
        if (days > 0) parts.Add($"{days}d");
        if (hours > 0) parts.Add($"{hours}h");
        if (minutes > 0) parts.Add($"{minutes}m");

        return parts.Count > 0 ? string.Join(" ", parts) : "0m";
    }

    /// <summary>
    /// Escapes special Markdown characters in a text string.
    /// </summary>
    /// <param name="text">The text to escape.</param>
    /// <returns>
    /// The escaped text with backslashes before special characters.
    /// Returns an empty string if the input is null.
    /// </returns>
    /// <remarks>
    /// Escapes the following characters: _ * [ ] ( ) ~ ` > # + - = | { } . ! \ /
    /// This is necessary for safe display of user-provided text in Markdown messages.
    /// </remarks>
    public static string EscapeMarkdown(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text ?? string.Empty;

        var sb = new StringBuilder(text.Length * 2);
        foreach (char c in text)
        {
            if (MarkdownSpecialChars.Contains(c))
                sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Checks if a time falls within a specified range.
    /// </summary>
    /// <param name="time">The time to check in HH:mm format.</param>
    /// <param name="start">The range start in HH:mm format.</param>
    /// <param name="end">The range end in HH:mm format.</param>
    /// <returns>
    /// <c>true</c> if the time is within the inclusive range; otherwise, <c>false</c>.
    /// </returns>
    public static bool IsTimeInRange(string time, string start, string end)
    {
        var t = ToMinutes(time);
        var s = ToMinutes(start);
        var e = ToMinutes(end);

        if (!t.HasValue || !s.HasValue || !e.HasValue)
            return false;

        return t >= s && t <= e;
    }

    /// <summary>
    /// Calculates the difference in minutes between two times.
    /// </summary>
    /// <param name="from">The start time in HH:mm format.</param>
    /// <param name="to">The end time in HH:mm format.</param>
    /// <returns>
    /// The difference in minutes. If the end time is earlier than the start time,
    /// the calculation wraps around to the next day.
    /// </returns>
    public static int GetTimeDifference(string from, string to)
    {
        var fromMinutes = ToMinutes(from);
        var toMinutes = ToMinutes(to);

        if (!fromMinutes.HasValue || !toMinutes.HasValue)
            return 0;

        var diff = toMinutes.Value - fromMinutes.Value;
        if (diff < 0) diff += TimeConstants.MinutesInDay;
        return diff;
    }

    /// <summary>
    /// Returns the current wall-clock time in the specified timezone.
    /// </summary>
    /// <param name="timeZone">The timezone in which to compute the current time.</param>
    /// <returns>
    /// A <see cref="DateTime"/> whose <see cref="DateTime.Kind"/> is <see cref="DateTimeKind.Unspecified"/>
    /// representing the current local time in <paramref name="timeZone"/>.
    /// </returns>
    /// <remarks>
    /// The schedule JSON contains wall-clock times in the users' local timezone. This helper
    /// computes "now" in that same timezone so that comparisons (e.g., "is this trip next?")
    /// are correct regardless of the server/container timezone, which is often UTC.
    /// </remarks>
    public static DateTime GetLocalNow(TimeZoneInfo timeZone)
    {
        var utcNow = DateTime.UtcNow;
        return TimeZoneInfo.ConvertTimeFromUtc(utcNow, timeZone);
    }

    /// <summary>
    /// Clears the internal cache used for time parsing.
    /// </summary>
    /// <remarks>
    /// This method is primarily useful for testing scenarios where cache consistency
    /// needs to be controlled. In normal operation, the cache should not need clearing.
    /// </remarks>
    public static void ClearCache()
    {
        _cache.Clear();
    }
}

