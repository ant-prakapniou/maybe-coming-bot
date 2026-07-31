using MaybeComingBot.Abstractions;
using MaybeComingBot.Constants;
using MaybeComingBot.Models;
using MaybeComingBot.Utils;
using Microsoft.Extensions.Logging;

namespace MaybeComingBot.Services;

public class ScheduleFormatter(
    IScheduleStorage storage,
    ILogger<ScheduleFormatter> logger)
{
    private readonly IScheduleStorage _storage = storage;
    private readonly ILogger<ScheduleFormatter> _logger = logger;

    public string FormatNearest(Stop stop)
    {
        var now = DateTime.Now;
        var day = (int)now.DayOfWeek;
        var trips = _storage.GetTripsForDay(stop.Id, day);

        if (trips is null || trips.Count == 0)
            return BotMessages.NoTripsToday(stop.Name, (DayOfWeek)day);

        var nowMinutes = now.Hour * TimeConstants.MinutesInHour + now.Minute;
        var sorted = trips
            .Select(t => new { Trip = t, Minutes = TimeHelper.ToMinutes(t.Time) })
            .Where(x => x.Minutes.HasValue)
            .OrderBy(x => x.Minutes)
            .ToList();

        if (sorted.Count == 0)
            return "⚠️ INVALID TIME FORMAT.";

        var index = sorted.FindIndex(x => x.Minutes >= nowMinutes);

        if (index == -1)
        {
            var nextDay = (DayOfWeek)((day + 1) % 7);
            var nextTrips = _storage.GetTripsForDay(stop.Id, (int)nextDay);
            var first = nextTrips?
                .Select(t => new { Trip = t, Minutes = TimeHelper.ToMinutes(t.Time) })
                .FirstOrDefault(x => x.Minutes.HasValue);

            return first is null
                ? BotMessages.NoTripsTomorrow(nextDay)
                : BotMessages.NearestTomorrow(nextDay, first.Trip.Time);
        }

        var nearest = sorted[index];
        var next = index + 1 < sorted.Count ? sorted[index + 1] : null;
        var wait = nearest.Minutes.Value - nowMinutes;
        if (wait < 0) wait += TimeConstants.DayMinutes;

        var response = $"🚌 *{TimeHelper.EscapeMarkdown(stop.Name)}*\n\n";
        response += $"🟢 *NEXT:* {nearest.Trip.Time}";
        if (!string.IsNullOrEmpty(nearest.Trip.Note))
            response += $" ({TimeHelper.EscapeMarkdown(nearest.Trip.Note)})";
        response += $"\n⏱ *IN {TimeHelper.FormatTimeSpan(wait)}*\n\n";

        if (next is not null)
        {
            response += $"🔵 *FOLLOWING:* {next.Trip.Time}";
            if (!string.IsNullOrEmpty(next.Trip.Note))
                response += $" ({TimeHelper.EscapeMarkdown(next.Trip.Note)})";
            if (next.Minutes.HasValue)
            {
                var nextWait = next.Minutes.Value - nowMinutes;
                if (nextWait < 0) nextWait += TimeConstants.DayMinutes;
                response += $"\n⏱ *IN {TimeHelper.FormatTimeSpan(nextWait)}*";
            }
        }
        else
        {
            response += "NO MORE TRIPS TODAY.";
        }

        return response;
    }

    public string FormatToday(Stop stop)
    {
        var now = DateTime.Now;
        var day = (int)now.DayOfWeek;
        var trips = _storage.GetTripsForDay(stop.Id, day);

        if (trips is null || trips.Count == 0)
            return BotMessages.NoTripsToday(stop.Name, (DayOfWeek)day);

        var nowMinutes = now.Hour * TimeConstants.MinutesInHour + now.Minute;
        var sorted = trips
            .Select(t => new { Trip = t, Minutes = TimeHelper.ToMinutes(t.Time) })
            .Where(x => x.Minutes.HasValue)
            .OrderBy(x => x.Minutes)
            .ToList();

        var response = $"📅 *TODAY ({now:dddd})*\n🚏 *{TimeHelper.EscapeMarkdown(stop.Name)}*\n\n";
        foreach (var item in sorted)
        {
            var mark = item.Minutes < nowMinutes ? "✅"
                : item.Minutes == nowMinutes ? "🔴"
                : "⬜";
            response += $"{mark} {item.Trip.Time}";
            if (!string.IsNullOrWhiteSpace(item.Trip.Note))
                response += $" ({TimeHelper.EscapeMarkdown(item.Trip.Note)})";
            response += "\n";
        }

        return response;
    }

    public string FormatWeek(Stop stop)
    {
        var response = $"📅 *WEEK SCHEDULE*\n🚏 *{TimeHelper.EscapeMarkdown(stop.Name)}*\n\n";

        for (int day = 0; day < 7; day++)
        {
            var dayName = (DayOfWeek)day;
            var trips = _storage.GetTripsForDay(stop.Id, day);
            response += $"*{dayName}*: ";

            if (trips is null || trips.Count == 0)
            {
                response += "NO TRIPS\n";
            }
            else
            {
                var times = string.Join(", ",
                    trips
                        .Select(t => new { Trip = t, Minutes = TimeHelper.ToMinutes(t.Time) })
                        .Where(x => x.Minutes.HasValue)
                        .OrderBy(x => x.Minutes)
                        .Select(x => x.Trip.Time));
                response += $"{times}\n";
            }
        }

        return response;
    }
}