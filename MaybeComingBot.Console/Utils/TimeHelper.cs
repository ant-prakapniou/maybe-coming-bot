using MaybeComingBot.Constants;
using System.Collections.Concurrent;
using System.Text;

namespace MaybeComingBot.Utils;

/// <summary>
/// Helper class for time-related operations
/// </summary>
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
    /// Converts time string (HH:mm) to minutes
    /// </summary>
    /// <param name="time">Time in HH:mm format</param>
    /// <returns>Minutes or null if invalid</returns>
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

            // Специальный случай: 24:00 - конец дня
            if (hours == 24 && minutes == 0)
                return TimeConstants.MaxMinutesInDay;

            // Проверка валидности
            if (hours >= 0 && hours <= 23 && minutes >= 0 && minutes <= 59)
                return hours * TimeConstants.MinutesInHour + minutes;

            return null;
        });
    }

    /// <summary>
    /// Converts time string to minutes with default value
    /// </summary>
    /// <param name="time">Time in HH:mm format</param>
    /// <param name="defaultValue">Default value if parsing fails</param>
    /// <returns>Minutes or default value</returns>
    public static int ToMinutesSafe(string time, int defaultValue = DefaultMinutes)
    {
        return ToMinutes(time) ?? defaultValue;
    }

    /// <summary>
    /// Tries to parse time string to minutes
    /// </summary>
    /// <param name="time">Time in HH:mm format</param>
    /// <param name="minutes">Parsed minutes</param>
    /// <returns>True if parsing succeeded</returns>
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
    /// Checks if time string is valid
    /// </summary>
    /// <param name="time">Time in HH:mm format</param>
    /// <returns>True if valid</returns>
    public static bool IsValidTime(string time)
    {
        return ToMinutes(time).HasValue;
    }

    /// <summary>
    /// Formats minutes to time string (HH:mm)
    /// </summary>
    /// <param name="minutes">Minutes since midnight</param>
    /// <returns>Formatted time string</returns>
    public static string FormatMinutes(int minutes)
    {
        if (minutes >= TimeConstants.MaxMinutesInDay)
            return "24:00";

        var hours = minutes / TimeConstants.MinutesInHour;
        var mins = minutes % TimeConstants.MinutesInHour;
        return $"{hours:D2}:{mins:D2}";
    }

    /// <summary>
    /// Formats time span in human-readable format
    /// </summary>
    /// <param name="totalMinutes">Total minutes</param>
    /// <returns>Human-readable time span</returns>
    public static string FormatTimeSpan(int totalMinutes)
    {
        if (totalMinutes < 0)
            return "NOW";

        if (totalMinutes == 0)
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
    /// Escapes special Markdown characters
    /// </summary>
    /// <param name="text">Text to escape</param>
    /// <returns>Escaped text</returns>
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
    /// Checks if time is within range
    /// </summary>
    /// <param name="time">Time to check</param>
    /// <param name="start">Range start</param>
    /// <param name="end">Range end</param>
    /// <returns>True if time is in range</returns>
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
    /// Calculates difference between two times
    /// </summary>
    /// <param name="from">Start time</param>
    /// <param name="to">End time</param>
    /// <returns>Difference in minutes</returns>
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
    /// Clears the cache (useful for testing)
    /// </summary>
    public static void ClearCache()
    {
        _cache.Clear();
    }
}