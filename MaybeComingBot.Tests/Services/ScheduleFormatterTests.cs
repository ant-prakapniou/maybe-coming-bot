using MaybeComingBot.Abstractions;
using MaybeComingBot.Models;
using MaybeComingBot.Services;
using MaybeComingBot.Utils;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MaybeComingBot.Tests.Services;

/// <summary>
/// Tests for <see cref="ScheduleFormatter"/> class.
/// </summary>
public class ScheduleFormatterTests
{
    private readonly Mock<IScheduleStorage> _storageMock;
    private readonly Mock<ILogger<ScheduleFormatter>> _loggerMock;
    private readonly ScheduleFormatter _formatter;

    public ScheduleFormatterTests()
    {
        _storageMock = new Mock<IScheduleStorage>();
        _loggerMock = new Mock<ILogger<ScheduleFormatter>>();
        _formatter = new ScheduleFormatter(_storageMock.Object, _loggerMock.Object);
    }

    // ============================================================
    // FormatNearest Tests
    // ============================================================

    [Fact]
    public void FormatNearest_WhenTripsExist_ReturnsNearestTrip()
    {
        // Arrange
        var stop = CreateStop("ST001", "Central Station");
        var now = DateTime.Now;
        var day = (int)now.DayOfWeek;

        // Создаем рейсы: один в прошлом, два в будущем
        var pastTime = now.AddHours(-2).ToString("HH:mm");
        var futureTime1 = now.AddHours(1).ToString("HH:mm");
        var futureTime2 = now.AddHours(2).ToString("HH:mm");

        var trips = new List<Trip>
        {
            new(pastTime, 42),
            new(futureTime1, 42),
            new(futureTime2, 42)
        };

        _storageMock
            .Setup(x => x.GetTripsForDay(stop.Id, day))
            .Returns(trips);

        // Act
        var result = _formatter.FormatNearest(stop);

        // Assert
        result.Should().Contain("🚌 *Central Station*");
        result.Should().Contain("🟢 *Next:*");
        result.Should().Contain(futureTime1);
        result.Should().NotContain("No trips in the next 7 days");
    }

    [Fact]
    public void FormatNearest_WhenNoTripsToday_FindsTomorrow()
    {
        // Arrange
        var stop = CreateStop("ST001", "Central Station");
        var today = (int)DateTime.Now.DayOfWeek;
        var tomorrow = (today + 1) % 7;

        _storageMock
            .Setup(x => x.GetTripsForDay(stop.Id, today))
            .Returns(new List<Trip>());

        _storageMock
            .Setup(x => x.GetTripsForDay(stop.Id, tomorrow))
            .Returns(new List<Trip> { new("09:00", 10) });

        // Act
        var result = _formatter.FormatNearest(stop);

        // Assert
        result.Should().Contain($"📅 *Next trip: {(DayOfWeek)tomorrow}*");
        result.Should().Contain("🟢 *Time:* 09:00");
    }

    [Fact]
    public void FormatNearest_WhenNoTripsAtAll_ReturnsNoTripsMessage()
    {
        // Arrange
        var stop = CreateStop("ST001", "Central Station");
        var today = (int)DateTime.Now.DayOfWeek;

        for (int i = 0; i < 7; i++)
        {
            _storageMock
                .Setup(x => x.GetTripsForDay(stop.Id, i))
                .Returns(new List<Trip>());
        }

        // Act
        var result = _formatter.FormatNearest(stop);

        // Assert
        result.Should().Contain("🚌 *Central Station*");
        result.Should().Contain("No trips in the next 7 days");
    }

    // ============================================================
    // FormatToday Tests
    // ============================================================

    [Fact]
    public void FormatToday_WithTrips_ReturnsFormattedSchedule()
    {
        // Arrange
        var stop = CreateStop("ST001", "Central Station");
        var day = (int)DateTime.Now.DayOfWeek;
        var trips = new List<Trip>
        {
            new("08:00", 10),
            new("09:00", 20),
            new("10:00", 30)
        };

        _storageMock
            .Setup(x => x.GetTripsForDay(stop.Id, day))
            .Returns(trips);

        // Act
        var result = _formatter.FormatToday(stop);

        // Assert
        result.Should().Contain("📅 *Today");
        result.Should().Contain("🚏 *Central Station*");
        result.Should().Contain("08:00");
        result.Should().Contain("09:00");
        result.Should().Contain("10:00");
    }

    [Fact]
    public void FormatToday_NoTrips_ReturnsNoTripsMessage()
    {
        // Arrange
        var stop = CreateStop("ST001", "Central Station");
        var day = (int)DateTime.Now.DayOfWeek;

        _storageMock
            .Setup(x => x.GetTripsForDay(stop.Id, day))
            .Returns(new List<Trip>());

        // Act
        var result = _formatter.FormatToday(stop);

        // Assert
        result.Should().Contain("no trips on");
    }

    [Fact]
    public void FormatToday_TripsMarkedCorrectly_ShowsPassedAndUpcoming()
    {
        // Arrange
        var stop = CreateStop("ST001", "Central Station");
        var day = (int)DateTime.Now.DayOfWeek;
        var now = DateTime.Now;
        var pastTime = now.AddHours(-2).ToString("HH:mm");
        var futureTime = now.AddHours(2).ToString("HH:mm");

        var trips = new List<Trip>
        {
            new(pastTime, 10),
            new(futureTime, 20)
        };

        _storageMock
            .Setup(x => x.GetTripsForDay(stop.Id, day))
            .Returns(trips);

        // Act
        var result = _formatter.FormatToday(stop);

        // Assert
        result.Should().Contain("✅ (passed)");
        result.Should().Contain("⬜");
    }

    // ============================================================
    // FormatWeek Tests
    // ============================================================

    [Fact]
    public void FormatWeek_WithTrips_ShowsAllDays()
    {
        // Arrange
        var stop = CreateStop("ST001", "Central Station");

        for (int day = 0; day < 7; day++)
        {
            var trips = day % 2 == 0
                ? new List<Trip> { new("10:00", 10) }
                : new List<Trip>();

            _storageMock
                .Setup(x => x.GetTripsForDay(stop.Id, day))
                .Returns(trips);
        }

        // Act
        var result = _formatter.FormatWeek(stop);

        // Assert
        result.Should().Contain("📅 *Week schedule*");
        result.Should().Contain("🚏 *Central Station*");

        for (int day = 0; day < 7; day++)
        {
            var dayName = (DayOfWeek)day;
            result.Should().Contain(dayName.ToString());

            if (day % 2 == 0)
                result.Should().Contain("10:00");
            else
                result.Should().Contain("🚫 No trips");
        }
    }

    [Fact]
    public void FormatWeek_NoTripsAtAll_ShowsNoTripsForAllDays()
    {
        // Arrange
        var stop = CreateStop("ST001", "Central Station");

        for (int day = 0; day < 7; day++)
        {
            _storageMock
                .Setup(x => x.GetTripsForDay(stop.Id, day))
                .Returns(new List<Trip>());
        }

        // Act
        var result = _formatter.FormatWeek(stop);

        // Assert
        result.Should().Contain("📅 *Week schedule*");
        result.Should().Contain("🚫 No trips");
    }

    // ============================================================
    // Helper Methods
    // ============================================================

    private static Stop CreateStop(string id, string name)
    {
        return new Stop
        {
            Id = id,
            Name = name,
            Schedule = new List<DaySchedule>()
        };
    }
}