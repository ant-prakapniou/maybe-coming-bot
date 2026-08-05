using MaybeComingBot.Abstractions;
using MaybeComingBot.Constants;
using MaybeComingBot.Models;
using MaybeComingBot.Utils;
using Microsoft.Extensions.Logging;
using System.Text;

namespace MaybeComingBot.Services;

/// <summary>
/// Formats schedule data into user-friendly text messages for display.
/// </summary>
/// <remarks>
/// This class provides methods for formatting schedule information in various
/// formats: nearest trip, today's full schedule, and weekly overview.
/// All outputs are formatted with Markdown for rich display in Telegram.
/// </remarks>
public class ScheduleFormatter(
    IScheduleStorage storage,
    TimeZoneInfo timeZone,
    ILogger<ScheduleFormatter> logger)
{
    private readonly IScheduleStorage _storage = storage;
    private readonly TimeZoneInfo _timeZone = timeZone;
    private readonly ILogger<ScheduleFormatter> _logger = logger;

    /// <summary>
    /// Formats the nearest upcoming trip for a given stop.
    /// </summary>
    /// <param name="stop">The stop to get the nearest trip for.</param>
    /// <returns>A formatted message showing the next trip with waiting time.</returns>
    /// <remarks>
    /// The logic:
    /// <list type="number">
    /// <item><description>Gets today's trips sorted by time</description></item>
    /// <item><description>Finds the next trip after current time</description></item>
    /// <item><description>If no more trips today, searches the next 7 days</description></item>
    /// <item><description>Shows the trip time and waiting duration</description></item>
    /// </list>
    /// </remarks>
    public string FormatNearest(Stop stop)
    {
        var now = TimeHelper.GetLocalNow(_timeZone);
        var day = (int)now.DayOfWeek;
        var nowMinutes = now.Hour * TimeConstants.MinutesInHour + now.Minute;

        var todayTrips = _storage.GetTripsForDay(stop.Id, day);
        var sortedTrips = GetSortedTrips(todayTrips);

        if (sortedTrips.Count is 0)
        {
            // Search in the following days
            var nextDayTrip = FindNextDayTrip(stop, day);
            if (nextDayTrip is null)
            {
                return $"🚌 *{TimeHelper.EscapeMarkdown(stop.Name)}*\n\n" +
                       "😔 No trips in the next 7 days.";
            }
            return FormatFutureDayTrip(stop, nextDayTrip.Value.Day, nextDayTrip.Value.Trip);
        }

        // Find the next trip today
        var nextIndex = sortedTrips.FindIndex(x => x.Minutes >= nowMinutes);

        if (nextIndex >= 0)
        {
            return FormatNextTrip(stop, sortedTrips, nextIndex, nowMinutes);
        }

        // No more trips today, search following days
        var nextDayTripAfter = FindNextDayTrip(stop, day);
        if (nextDayTripAfter is null)
        {
            return $"🚌 *{TimeHelper.EscapeMarkdown(stop.Name)}*\n\n" +
                   "😔 No more trips today or in the next 7 days.";
        }

        return FormatFutureDayTrip(stop, nextDayTripAfter.Value.Day, nextDayTripAfter.Value.Trip);
    }

    /// <summary>
    /// Formats the full schedule for today at a given stop.
    /// </summary>
    /// <param name="stop">The stop to format the schedule for.</param>
    /// <returns>A formatted message showing all trips for today with status indicators.</returns>
    /// <remarks>
    /// Each trip is marked with:
    /// <list type="bullet">
    /// <item><description>✅ (passed) - trips that have already occurred</description></item>
    /// <item><description>🔴 NOW - the current trip at this exact minute</description></item>
    /// <item><description>⬜ - upcoming trips</description></item>
    /// </list>
    /// </remarks>
    public string FormatToday(Stop stop)
    {
        var now = TimeHelper.GetLocalNow(_timeZone);
        var day = (int)now.DayOfWeek;
        var trips = _storage.GetTripsForDay(stop.Id, day);
        var sortedTrips = GetSortedTrips(trips);

        if (sortedTrips.Count is 0)
            return BotMessages.NoTripsToday(stop.Name, (DayOfWeek)day);

        var nowMinutes = now.Hour * TimeConstants.MinutesInHour + now.Minute;
        var response = new StringBuilder();
        response.AppendLine($"📅 *Today ({now:dddd})*");
        response.AppendLine($"🚏 *{TimeHelper.EscapeMarkdown(stop.Name)}*");
        response.AppendLine();

        foreach (var item in sortedTrips)
        {
            string mark;
            if (item.Minutes < nowMinutes)
                mark = "✅ (passed)";
            else if (item.Minutes == nowMinutes)
                mark = "🔴 now";
            else
                mark = "⬜";

            response.Append($"{mark} {item.Trip.Time}");
            if (!string.IsNullOrWhiteSpace(item.Trip.Note))
                response.Append($" ({TimeHelper.EscapeMarkdown(item.Trip.Note)})");
            response.AppendLine();
        }

        return response.ToString();
    }

    /// <summary>
    /// Formats the full weekly schedule for a given stop.
    /// </summary>
    /// <param name="stop">The stop to format the weekly schedule for.</param>
    /// <returns>A formatted message showing all trips for each day of the week.</returns>
    public string FormatWeek(Stop stop)
    {
        var response = new StringBuilder();
        response.AppendLine($"📅 *Week schedule*");
        response.AppendLine($"🚏 *{TimeHelper.EscapeMarkdown(stop.Name)}*");
        response.AppendLine();

        for (int day = default; day < 7; day++)
        {
            var dayName = (DayOfWeek)day;
            var trips = _storage.GetTripsForDay(stop.Id, day);
            var sortedTrips = GetSortedTrips(trips);

            response.Append($"*{dayName}*: ");

            if (sortedTrips.Count is 0)
            {
                response.AppendLine("🚫 No trips");
            }
            else
            {
                var times = string.Join(", ", sortedTrips.Select(x => x.Trip.Time));
                response.AppendLine(times);
            }

            // Add separator between days
            if (day < 6)
                response.AppendLine("━━━━━━━━━━━━━━━━━━━");
        }

        return response.ToString();
    }

    /// <summary>
    /// Sorts and converts trips to a list with minutes since midnight.
    /// </summary>
    /// <param name="trips">The trips to process.</param>
    /// <returns>A sorted list of trips with their minutes since midnight.</returns>
    private static List<(Trip Trip, int Minutes)> GetSortedTrips(IReadOnlyList<Trip>? trips)
    {
        if (trips is null || trips.Count is 0)
            return [];

        return [.. trips
            .Where(t => t != null)
            .Select(t => (Trip: t, Minutes: TimeHelper.ToMinutes(t.Time)))
            .Where(x => x.Minutes.HasValue)
            .Select(x => (x.Trip, Minutes: x.Minutes!.Value))
            .OrderBy(x => x.Minutes)];
    }

    /// <summary>
    /// Formats the next trip with waiting time and following trip.
    /// </summary>
    private static string FormatNextTrip(Stop stop, List<(Trip Trip, int Minutes)> sortedTrips, int index, int nowMinutes)
    {
        var nearest = sortedTrips[index];
        var wait = nearest.Minutes - nowMinutes;
        if (wait < 0) wait += TimeConstants.MinutesInDay;

        var response = new StringBuilder();
        response.AppendLine($"🚌 *{TimeHelper.EscapeMarkdown(stop.Name)}*");
        response.AppendLine();
        response.Append($"🟢 *Next:* {nearest.Trip.Time}");
        if (!string.IsNullOrEmpty(nearest.Trip.Note))
            response.Append($" ({TimeHelper.EscapeMarkdown(nearest.Trip.Note)})");
        response.AppendLine($"\n⏱ In {TimeHelper.FormatTimeSpan(wait)}");
        response.AppendLine();

        // Following trip
        var nextIndex = index + 1;
        if (nextIndex < sortedTrips.Count)
        {
            var next = sortedTrips[nextIndex];
            response.Append($"🔵 *Following:* {next.Trip.Time}");
            if (!string.IsNullOrEmpty(next.Trip.Note))
                response.Append($" ({TimeHelper.EscapeMarkdown(next.Trip.Note)})");

            var nextWait = next.Minutes - nowMinutes;
            if (nextWait < 0) nextWait += TimeConstants.MinutesInDay;
            response.Append($"\n⏱ In {TimeHelper.FormatTimeSpan(nextWait)}");
        }
        else
        {
            response.Append("No more trips today.");
        }

        return response.ToString();
    }

    /// <summary>
    /// Finds the first trip in the following days.
    /// </summary>
    /// <param name="stop">The stop to search.</param>
    /// <param name="startDay">The starting day index (0-6).</param>
    /// <returns>A tuple with the day and trip, or null if none found.</returns>
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

    /// <summary>
    /// Formats a trip on a future day.
    /// </summary>
    private static string FormatFutureDayTrip(Stop stop, DayOfWeek day, Trip trip)
    {
        var response = new StringBuilder();
        response.AppendLine($"🚌 *{TimeHelper.EscapeMarkdown(stop.Name)}*");
        response.AppendLine();
        response.AppendLine($"📅 *Next trip: {day}*");
        response.Append($"🟢 *Time:* {trip.Time}");
        if (!string.IsNullOrEmpty(trip.Note))
            response.Append($" ({TimeHelper.EscapeMarkdown(trip.Note)})");
        return response.ToString();
    }
}

