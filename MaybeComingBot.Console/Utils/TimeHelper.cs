using MaybeComingBot.Constants;
using System.Text;

namespace MaybeComingBot.Utils;

public static class TimeHelper
{
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
            if (hours == 24 && minutes == 0)
                return TimeConstants.MaxMinutesInDay;

            if (hours >= 0 && hours <= 23 && minutes >= 0 && minutes <= 59)
                return hours * TimeConstants.MinutesInHour + minutes;

            return null;
        }

        return null;
    }

    public static int ToMinutesSafe(string time)
    {
        return ToMinutes(time) ?? -1;
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

        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;

        if (hours == 0)
            return $"{minutes}m";
        if (minutes == 0)
            return $"{hours}h";
        return $"{hours}h {minutes}m";
    }

    public static string EscapeMarkdown(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var sb = new StringBuilder(text.Length * 2);
        foreach (char c in text)
        {
            if ("_*[]()~`>#+-=|{}.!".Contains(c))
                sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
    }
}