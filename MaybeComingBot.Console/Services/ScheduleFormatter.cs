using MaybeComingBot.Abstractions;
using MaybeComingBot.Constants;
using MaybeComingBot.Models;
using MaybeComingBot.Utils;
using Microsoft.Extensions.Logging;
using System.Text;

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
        {
            // Ищем в следующие дни
            var nextDayTrip = FindNextDayTrip(stop, day);
            if (nextDayTrip == null)
            {
                return $"🚌 *{TimeHelper.EscapeMarkdown(stop.Name)}*\n\n" +
                       "❌ NO TRIPS IN THE NEAR FUTURE (7 DAYS).";
            }
            return FormatFutureDayTrip(stop, nextDayTrip.Value.Day, nextDayTrip.Value.Trip);
        }

        // Ищем следующий рейс сегодня
        var nextIndex = sortedTrips.FindIndex(x => x.Minutes >= nowMinutes);

        if (nextIndex >= 0)
        {
            return FormatNextTrip(stop, sortedTrips, nextIndex, nowMinutes);
        }

        // Если сегодня нет рейсов после текущего времени, ищем в следующие дни
        var nextDayTripAfter = FindNextDayTrip(stop, day);
        if (nextDayTripAfter == null)
        {
            return $"🚌 *{TimeHelper.EscapeMarkdown(stop.Name)}*\n\n" +
                   "❌ NO MORE TRIPS TODAY AND NEXT 7 DAYS.";
        }

        return FormatFutureDayTrip(stop, nextDayTripAfter.Value.Day, nextDayTripAfter.Value.Trip);
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
        var response = new StringBuilder();
        response.AppendLine($"📅 *TODAY ({now:dddd})*");
        response.AppendLine($"🚏 *{TimeHelper.EscapeMarkdown(stop.Name)}*");
        response.AppendLine();

        foreach (var item in sortedTrips)
        {
            string mark;
            if (item.Minutes < nowMinutes)
                mark = "✅ (PASSED)";
            else if (item.Minutes == nowMinutes)
                mark = "🔴 NOW";
            else
                mark = "⬜";

            response.Append($"{mark} {item.Trip.Time}");
            if (!string.IsNullOrWhiteSpace(item.Trip.Note))
                response.Append($" ({TimeHelper.EscapeMarkdown(item.Trip.Note)})");
            response.AppendLine();
        }

        return response.ToString();
    }

    public string FormatWeek(Stop stop)
    {
        var response = new StringBuilder();
        response.AppendLine($"📅 *WEEK SCHEDULE*");
        response.AppendLine($"🚏 *{TimeHelper.EscapeMarkdown(stop.Name)}*");
        response.AppendLine();

        for (int day = 0; day < 7; day++)
        {
            var dayName = (DayOfWeek)day;
            var trips = _storage.GetTripsForDay(stop.Id, day);
            var sortedTrips = GetSortedTrips(trips);

            response.Append($"*{dayName}*: ");

            if (sortedTrips.Count == 0)
            {
                response.AppendLine("🚫 NO TRIPS");
            }
            else
            {
                var times = string.Join(", ", sortedTrips.Select(x => x.Trip.Time));
                response.AppendLine(times);
            }

            // Добавляем разделитель между днями
            if (day < 6)
                response.AppendLine("━━━━━━━━━━━━━━━━━━━");
        }

        return response.ToString();
    }

    // Вспомогательные методы

    private static List<(Trip Trip, int Minutes)> GetSortedTrips(IReadOnlyList<Trip>? trips)
    {
        if (trips == null || trips.Count == 0)
            return new List<(Trip, int)>();

        return trips
            .Where(t => t != null)
            .Select(t => (Trip: t, Minutes: TimeHelper.ToMinutes(t.Time)))
            .Where(x => x.Minutes.HasValue)
            .Select(x => (x.Trip, Minutes: x.Minutes.Value))
            .OrderBy(x => x.Minutes)
            .ToList();
    }

    private string FormatNextTrip(Stop stop, List<(Trip Trip, int Minutes)> sortedTrips, int index, int nowMinutes)
    {
        var nearest = sortedTrips[index];
        var wait = nearest.Minutes - nowMinutes;
        if (wait < 0) wait += TimeConstants.MinutesInDay;

        var response = new StringBuilder();
        response.AppendLine($"🚌 *{TimeHelper.EscapeMarkdown(stop.Name)}*");
        response.AppendLine();
        response.Append($"🟢 *NEXT:* {nearest.Trip.Time}");
        if (!string.IsNullOrEmpty(nearest.Trip.Note))
            response.Append($" ({TimeHelper.EscapeMarkdown(nearest.Trip.Note)})");
        response.AppendLine($"\n⏱ *IN {TimeHelper.FormatTimeSpan(wait)}*");
        response.AppendLine();

        // Следующий рейс
        var nextIndex = index + 1;
        if (nextIndex < sortedTrips.Count)
        {
            var next = sortedTrips[nextIndex];
            response.Append($"🔵 *FOLLOWING:* {next.Trip.Time}");
            if (!string.IsNullOrEmpty(next.Trip.Note))
                response.Append($" ({TimeHelper.EscapeMarkdown(next.Trip.Note)})");

            var nextWait = next.Minutes - nowMinutes;
            if (nextWait < 0) nextWait += TimeConstants.MinutesInDay;
            response.Append($"\n⏱ *IN {TimeHelper.FormatTimeSpan(nextWait)}*");
        }
        else
        {
            response.Append("NO MORE TRIPS TODAY.");
        }

        return response.ToString();
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
                return ((DayOfWeek)day, sortedTrips[0].Trip);
            }
        }
        return null;
    }

    private string FormatFutureDayTrip(Stop stop, DayOfWeek day, Trip trip)
    {
        var response = new StringBuilder();
        response.AppendLine($"🚌 *{TimeHelper.EscapeMarkdown(stop.Name)}*");
        response.AppendLine();
        response.AppendLine($"📅 *NEXT TRIP: {day}*");
        response.Append($"🟢 *TIME:* {trip.Time}");
        if (!string.IsNullOrEmpty(trip.Note))
            response.Append($" ({TimeHelper.EscapeMarkdown(trip.Note)})");
        return response.ToString();
    }
}