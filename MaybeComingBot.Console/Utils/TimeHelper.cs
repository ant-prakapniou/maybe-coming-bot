using MaybeComingBot.Constants;

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
}