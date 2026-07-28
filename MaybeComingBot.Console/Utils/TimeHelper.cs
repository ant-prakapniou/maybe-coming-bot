namespace MaybeComingBot.Console.Utils;

public static class TimeHelper
{
    public static int ToMinutes(string time)
    {
        if (string.IsNullOrEmpty(time))
            return 0;

        var parts = time.Split(':');
        if (parts.Length != 2)
            return 0;

        if (int.TryParse(parts[0], out int hours) && int.TryParse(parts[1], out int minutes))
        {
            return hours * 60 + minutes;
        }

        return 0;
    }
}
