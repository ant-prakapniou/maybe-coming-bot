using MaybeComingBot.Constants;
using System.Text;

namespace MaybeComingBot.Utils;

public static class TimeHelper
{
    private static readonly HashSet<char> MarkdownSpecialChars = new()
    {
        '_', '*', '[', ']', '(', ')', '~', '`', '>', '#', '+', '-', '=', '|', '{', '}', '.', '!'
    };

    public static int? ToMinutes(string time)
    {
        if (string.IsNullOrWhiteSpace(time))
            return null;

        var parts = time.Split(':');
        if (parts.Length != 2)
            return null;

        if (int.TryParse(parts[0], out int hours) &&
            int.TryParse(parts[1], out int minutes))
        {
            // Специальный случай: 24:00 - конец дня
            if (hours == 24 && minutes == 0)
                return TimeConstants.MaxMinutesInDay;

            // Проверка валидности
            if (hours >= 0 && hours <= 23 && minutes >= 0 && minutes <= 59)
                return hours * TimeConstants.MinutesInHour + minutes;

            return null;
        }

        return null;
    }

    public static int ToMinutesSafe(string time)
    {
        return ToMinutes(time) ?? int.MinValue;
    }

    public static bool TryParseMinutes(string time, out int minutes)
    {
        var result = ToMinutes(time);
        if (result.HasValue)
        {
            minutes = result.Value;
            return true;
        }
        minutes = 0;
        return false;
    }

    public static bool IsValidTime(string time)
    {
        return ToMinutes(time).HasValue;
    }

    public static string FormatMinutes(int minutes)
    {
        if (minutes >= TimeConstants.MaxMinutesInDay)
            return "24:00";

        var hours = minutes / TimeConstants.MinutesInHour;
        var mins = minutes % TimeConstants.MinutesInHour;
        return $"{hours:D2}:{mins:D2}";
    }

    public static string FormatTimeSpan(int totalMinutes)
    {
        if (totalMinutes < 0)
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

    public static string EscapeMarkdown(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var sb = new StringBuilder(text.Length * 2);
        foreach (char c in text)
        {
            if (MarkdownSpecialChars.Contains(c))
                sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
    }
}