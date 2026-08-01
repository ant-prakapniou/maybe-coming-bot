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
        var nowMinutes = now.Hour * TimeConstants.MinutesInHour + now.Minute;

        var todayTrips = _storage.GetTripsForDay(stop.Id, day);
        var sortedTrips = GetSortedTrips(todayTrips);

        if (sortedTrips.Count == 0)
            return BotMessages.NoTripsToday(stop.Name, (DayOfWeek)day);

        // Ищем следующий рейс сегодня
        var nextIndex = sortedTrips.FindIndex(x => x.Minutes >= nowMinutes);

        if (nextIndex >= 0)
        {
            return FormatNextTrip(stop, sortedTrips, nextIndex, nowMinutes);
        }

        // Если сегодня нет рейсов, ищем в следующие дни
        var nextDayTrip = FindNextDayTrip(stop, day);
        if (nextDayTrip == null)
        {
            return "🚌 *" + TimeHelper.EscapeMarkdown(stop.Name) + "*\n\n" +
                   "❌ NO TRIPS IN THE NEAR FUTURE (7 DAYS).";
        }

        return FormatFutureDayTrip(stop, nextDayTrip.Value.Day, nextDayTrip.Value.Trip);
    }

    public string FormatToday(Stop stop)
    {
        var now = DateTime.Now;
        var day = (int)now.DayOfWeek;
        var trips = _storage.GetTripsForDay(stop.Id, day);
        var sortedTrips = GetSortedTrips(trips);

        if (sortedTrips.Count == 0)
            return BotMessages.NoTripsToday(stop.Name, (DayOfWeek)day);

        var nowMinutes = now.Hour * TimeConstants.MinutesInHour + now.Minute;
        var response = $"📅 *TODAY ({now:dddd})*\n🚏 *{TimeHelper.EscapeMarkdown(stop.Name)}*\n\n";

        foreach (var item in sortedTrips)
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
            var sortedTrips = GetSortedTrips(trips);

            response += $"*{dayName}*: ";

            if (sortedTrips.Count == 0)
            {
                response += "NO TRIPS\n";
            }
            else
            {
                var times = string.Join(", ", sortedTrips.Select(x => x.Trip.Time));
                response += $"{times}\n";
            }
        }

        return response;
    }

    // Вспомогательные методы

    // Возвращаем НЕ-nullable кортеж (int Minutes, а не int? Minutes)
    private static List<(Trip Trip, int Minutes)> GetSortedTrips(IReadOnlyList<Trip>? trips)
    {
        if (trips == null || trips.Count == 0)
            return new List<(Trip, int)>();

        return trips
            .Where(t => t != null)
            .Select(t => (Trip: t, Minutes: TimeHelper.ToMinutes(t.Time)))
            .Where(x => x.Minutes.HasValue)
            .Select(x => (Trip: x.Trip, Minutes: x.Minutes.GetValueOrDefault())) // Безопасно извлекаем Value
            .OrderBy(x => x.Minutes)
            .ToList();
    }

    private string FormatNextTrip(Stop stop, List<(Trip Trip, int Minutes)> sortedTrips, int index, int nowMinutes)
    {
        var nearest = sortedTrips[index];
        var wait = nearest.Minutes - nowMinutes;
        if (wait < 0) wait += TimeConstants.MinutesInDay;

        var response = $"🚌 *{TimeHelper.EscapeMarkdown(stop.Name)}*\n\n";
        response += $"🟢 *NEXT:* {nearest.Trip.Time}";
        if (!string.IsNullOrEmpty(nearest.Trip.Note))
            response += $" ({TimeHelper.EscapeMarkdown(nearest.Trip.Note)})";
        response += $"\n⏱ *IN {TimeHelper.FormatTimeSpan(wait)}*\n\n";

        // Следующий рейс
        var nextIndex = index + 1;
        if (nextIndex < sortedTrips.Count)
        {
            var next = sortedTrips[nextIndex];
            response += $"🔵 *FOLLOWING:* {next.Trip.Time}";
            if (!string.IsNullOrEmpty(next.Trip.Note))
                response += $" ({TimeHelper.EscapeMarkdown(next.Trip.Note)})";

            var nextWait = next.Minutes - nowMinutes;
            if (nextWait < 0) nextWait += TimeConstants.MinutesInDay;
            response += $"\n⏱ *IN {TimeHelper.FormatTimeSpan(nextWait)}*";
        }
        else
        {
            response += "NO MORE TRIPS TODAY.";
        }

        return response;
    }

    private (DayOfWeek Day, Trip Trip)? FindNextDayTrip(Stop stop, int startDay)
    {
        for (int offset = 1; offset < 7; offset++)
        {
            var day = (startDay + offset) % 7;
            var trips = _storage.GetTripsForDay(stop.Id, day);
            var sortedTrips = GetSortedTrips(trips);

            if (sortedTrips.Count > 0)
            {
                (DayOfWeek Day, Trip Trip) result = ((DayOfWeek)day, sortedTrips[0].Trip);
                return result;
            }
        }
        return null;
    }

    private string FormatFutureDayTrip(Stop stop, DayOfWeek day, Trip trip)
    {
        var response = $"🚌 *{TimeHelper.EscapeMarkdown(stop.Name)}*\n\n";
        response += $"📅 *NEXT TRIP: {day}*\n";
        response += $"🟢 *TIME:* {trip.Time}";
        if (!string.IsNullOrEmpty(trip.Note))
            response += $" ({TimeHelper.EscapeMarkdown(trip.Note)})";
        return response;
    }
}