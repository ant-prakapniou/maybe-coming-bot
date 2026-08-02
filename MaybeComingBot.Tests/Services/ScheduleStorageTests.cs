using MaybeComingBot.Abstractions;
using MaybeComingBot.Models;
using MaybeComingBot.Services;
using MaybeComingBot.Utils;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MaybeComingBot.Tests.Services;

public class ScheduleStorageTests
{
    private readonly ScheduleStorage _storage;
    private readonly ILogger<ScheduleStorage> _logger;

    public ScheduleStorageTests()
    {
        _logger = Mock.Of<ILogger<ScheduleStorage>>();
        _storage = new ScheduleStorage(_logger);
    }

    // ============================================================
    // Update Tests
    // ============================================================

    [Fact]
    public void Update_ValidData_UpdatesStorage()
    {
        // Arrange
        var data = CreateScheduleRoot();

        // Act
        _storage.Update(data);

        // Assert
        _storage.HasData().Should().BeTrue();
        _storage.StopCount.Should().Be(2);
        _storage.CachedTripsCount.Should().BeGreaterThan(0);
        _storage.IsLoaded.Should().BeTrue();
    }

    [Fact]
    public void Update_EmptyData_ClearsStorage()
    {
        // Arrange
        var data = new ScheduleRoot();

        // Act
        _storage.Update(data);

        // Assert
        _storage.HasData().Should().BeFalse();
        _storage.StopCount.Should().Be(0);
        _storage.CachedTripsCount.Should().Be(0);
        _storage.IsLoaded.Should().BeFalse();
    }

    [Fact]
    public void Update_NullData_DoesNothingAndLogsWarning()
    {
        // Act
        _storage.Update(null!);

        // Assert
        _storage.HasData().Should().BeFalse();
        _storage.StopCount.Should().Be(0);
    }

    [Fact]
    public void Update_WithDuplicateStopIds_SkipsDuplicates()
    {
        // Arrange
        var data = new ScheduleRoot
        {
            Stops = new List<Stop>
            {
                new Stop { Id = "ST001", Name = "Stop 1" },
                new Stop { Id = "ST001", Name = "Stop 1 Duplicate" }
            }
        };

        // Act
        _storage.Update(data);

        // Assert
        _storage.StopCount.Should().Be(1);
        _storage.GetStop("ST001").Should().NotBeNull();
        _storage.GetStop("ST001")!.Name.Should().Be("Stop 1");
    }

    // ============================================================
    // LastUpdated Tests
    // ============================================================

    [Fact]
    public void LastUpdated_BeforeUpdate_ReturnsMinValue()
    {
        // Act
        var lastUpdated = _storage.LastUpdated;

        // Assert
        lastUpdated.Should().Be(DateTime.MinValue);
    }

    [Fact]
    public void Update_WithValidData_SetsLastUpdatedToUtcNow()
    {
        // Arrange
        var data = CreateScheduleRoot();
        var before = DateTime.UtcNow;

        // Act
        _storage.Update(data);

        // Assert
        _storage.LastUpdated.Should().BeAfter(before);
        _storage.LastUpdated.Kind.Should().Be(DateTimeKind.Utc);
        _storage.LastUpdated.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Update_WithEmptyData_DoesNotAdvanceLastUpdated()
    {
        // Arrange
        var data = CreateScheduleRoot();
        _storage.Update(data);
        var lastUpdatedAfterValid = _storage.LastUpdated;

        // Act
        _storage.Update(new ScheduleRoot());

        // Assert
        _storage.LastUpdated.Should().Be(lastUpdatedAfterValid);
    }

    [Fact]
    public void Clear_ResetsLastUpdatedToMinValue()
    {
        // Arrange
        var data = CreateScheduleRoot();
        _storage.Update(data);
        _storage.LastUpdated.Should().NotBe(DateTime.MinValue);

        // Act
        _storage.Clear();

        // Assert
        _storage.LastUpdated.Should().Be(DateTime.MinValue);
    }

    // ============================================================
    // GetAllStops Tests
    // ============================================================

    [Fact]
    public void GetAllStops_WhenDataLoaded_ReturnsAllStops()
    {
        // Arrange
        var data = CreateScheduleRoot();
        _storage.Update(data);

        // Act
        var stops = _storage.GetAllStops();

        // Assert
        stops.Should().HaveCount(2);
        stops.Select(s => s.Id).Should().Contain(new[] { "ST001", "ST002" });
    }

    [Fact]
    public void GetAllStops_WhenNoData_ReturnsEmptyList()
    {
        // Act
        var stops = _storage.GetAllStops();

        // Assert
        stops.Should().BeEmpty();
    }

    // ============================================================
    // GetStop Tests
    // ============================================================

    [Fact]
    public void GetStop_ExistingStop_ReturnsStop()
    {
        // Arrange
        var data = CreateScheduleRoot();
        _storage.Update(data);

        // Act
        var stop = _storage.GetStop("ST001");

        // Assert
        stop.Should().NotBeNull();
        stop!.Id.Should().Be("ST001");
        stop!.Name.Should().Be("Central Station");
    }

    [Fact]
    public void GetStop_NonExistingStop_ReturnsNull()
    {
        // Arrange
        var data = CreateScheduleRoot();
        _storage.Update(data);

        // Act
        var stop = _storage.GetStop("ST999");

        // Assert
        stop.Should().BeNull();
    }

    [Fact]
    public void GetStop_EmptyId_ReturnsNull()
    {
        // Act
        var stop = _storage.GetStop(string.Empty);

        // Assert
        stop.Should().BeNull();
    }

    [Fact]
    public void GetStop_NullId_ReturnsNull()
    {
        // Act
        var stop = _storage.GetStop(null!);

        // Assert
        stop.Should().BeNull();
    }

    [Fact]
    public void GetStop_WhenNoData_ReturnsNull()
    {
        // Act
        var stop = _storage.GetStop("ST001");

        // Assert
        stop.Should().BeNull();
    }

    // ============================================================
    // GetTripsForDay Tests
    // ============================================================

    [Fact]
    public void GetTripsForDay_ExistingStopAndDay_ReturnsTrips()
    {
        // Arrange
        var data = CreateScheduleRoot();
        _storage.Update(data);

        // Act
        var trips = _storage.GetTripsForDay("ST001", 0);

        // Assert
        trips.Should().NotBeNull();
        trips.Should().HaveCount(2);
        trips![0].Time.Should().Be("08:00");
        trips![1].Time.Should().Be("09:00");
    }

    [Fact]
    public void GetTripsForDay_StopHasNoTripsOnDay_ReturnsEmptyList()
    {
        // Arrange
        var data = CreateScheduleRoot();
        _storage.Update(data);

        // Act
        var trips = _storage.GetTripsForDay("ST002", 0);

        // Assert
        trips.Should().NotBeNull();
        trips.Should().BeEmpty();
    }

    [Fact]
    public void GetTripsForDay_InvalidStopId_ReturnsEmptyList()
    {
        // Arrange
        var data = CreateScheduleRoot();
        _storage.Update(data);

        // Act
        var trips = _storage.GetTripsForDay("ST999", 0);

        // Assert
        trips.Should().NotBeNull();
        trips.Should().BeEmpty();
    }

    [Fact]
    public void GetTripsForDay_EmptyStopId_ReturnsNull()
    {
        // Act
        var trips = _storage.GetTripsForDay(string.Empty, 0);

        // Assert
        trips.Should().BeNull();
    }

    [Fact]
    public void GetTripsForDay_WhenNoData_ReturnsNull()
    {
        // Act
        var trips = _storage.GetTripsForDay("ST001", 0);

        // Assert
        trips.Should().BeNull();
    }

    // ============================================================
    // HasData Tests
    // ============================================================

    [Fact]
    public void HasData_AfterUpdate_ReturnsTrue()
    {
        // Arrange
        var data = CreateScheduleRoot();

        // Act
        _storage.Update(data);

        // Assert
        _storage.HasData().Should().BeTrue();
    }

    [Fact]
    public void HasData_BeforeUpdate_ReturnsFalse()
    {
        // Act
        var result = _storage.HasData();

        // Assert
        result.Should().BeFalse();
    }

    // ============================================================
    // Clear Tests
    // ============================================================

    [Fact]
    public void Clear_RemovesAllData()
    {
        // Arrange
        var data = CreateScheduleRoot();
        _storage.Update(data);
        _storage.HasData().Should().BeTrue();

        // Act
        _storage.Clear();

        // Assert
        _storage.HasData().Should().BeFalse();
        _storage.StopCount.Should().Be(0);
        _storage.CachedTripsCount.Should().Be(0);
        _storage.IsLoaded.Should().BeFalse();
        _storage.GetAllStops().Should().BeEmpty();
    }

    [Fact]
    public void Clear_WhenAlreadyEmpty_DoesNothing()
    {
        // Act
        var act = () => _storage.Clear();

        // Assert
        act.Should().NotThrow();
        _storage.HasData().Should().BeFalse();
    }

    // ============================================================
    // Property Tests
    // ============================================================

    [Fact]
    public void StopCount_AfterUpdate_ReturnsCorrectCount()
    {
        // Arrange
        var data = CreateScheduleRoot();

        // Act
        _storage.Update(data);

        // Assert
        _storage.StopCount.Should().Be(2);
    }

    [Fact]
    public void StopCount_BeforeUpdate_ReturnsZero()
    {
        // Act
        var count = _storage.StopCount;

        // Assert
        count.Should().Be(0);
    }

    [Fact]
    public void CachedTripsCount_AfterUpdate_ReturnsCorrectCount()
    {
        // Arrange
        var data = CreateScheduleRoot();

        // Act
        _storage.Update(data);

        // Assert
        _storage.CachedTripsCount.Should().Be(1); // Only ST001 has trips on day 0
    }

    [Fact]
    public void CachedTripsCount_BeforeUpdate_ReturnsZero()
    {
        // Act
        var count = _storage.CachedTripsCount;

        // Assert
        count.Should().Be(0);
    }

    [Fact]
    public void IsLoaded_AfterUpdate_ReturnsTrue()
    {
        // Arrange
        var data = CreateScheduleRoot();

        // Act
        _storage.Update(data);

        // Assert
        _storage.IsLoaded.Should().BeTrue();
    }

    [Fact]
    public void IsLoaded_BeforeUpdate_ReturnsFalse()
    {
        // Act
        var loaded = _storage.IsLoaded;

        // Assert
        loaded.Should().BeFalse();
    }

    // ============================================================
    // Thread Safety Tests
    // ============================================================

    [Fact]
    public async Task ConcurrentReads_WhileUpdating_DoesNotThrow()
    {
        // Arrange
        var data = CreateScheduleRoot();

        // Act
        var tasks = new List<Task>();
        var cts = new CancellationTokenSource();

        // Start concurrent reads
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    var stops = _storage.GetAllStops();
                    var stop = _storage.GetStop("ST001");
                    var trips = _storage.GetTripsForDay("ST001", 0);
                    _ = _storage.HasData();
                    _ = _storage.StopCount;
                    _ = _storage.CachedTripsCount;
                    _ = _storage.IsLoaded;
                }
            }, TestContext.Current.CancellationToken));
        }

        // Perform updates concurrently
        for (int i = 0; i < 5; i++)
        {
            _storage.Update(data);
            await Task.Delay(10, TestContext.Current.CancellationToken);
            _storage.Clear();
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        cts.Cancel();
        await Task.WhenAll(tasks);

        // Assert - no exceptions thrown
        Assert.True(true);
    }

    // ============================================================
    // Clone Tests
    // ============================================================

    [Fact]
    public void Update_ClonesData_SoOriginalCannotBeModified()
    {
        // Arrange
        var data = CreateScheduleRoot();
        var originalStop = data.Stops.First();

        // Act
        _storage.Update(data);
        originalStop.Name = "Modified Name"; // Modify original

        // Assert
        var storedStop = _storage.GetStop("ST001");
        storedStop!.Name.Should().Be("Central Station"); // Not modified
    }

    [Fact]
    public void GetStop_ReturnsSameInstance_NotCloned()
    {
        // Arrange
        var data = CreateScheduleRoot();
        _storage.Update(data);

        // Act
        var stop1 = _storage.GetStop("ST001");
        var stop2 = _storage.GetStop("ST001");

        // Assert
        stop1.Should().BeSameAs(stop2);
    }

    // ============================================================
    // Helper Methods
    // ============================================================

    private static ScheduleRoot CreateScheduleRoot()
    {
        return new ScheduleRoot
        {
            Stops = new List<Stop>
            {
                new Stop
                {
                    Id = "ST001",
                    Name = "Central Station",
                    Schedule = new List<DaySchedule>
                    {
                        new DaySchedule
                        {
                            Day = 0,
                            Trips = new List<Trip>
                            {
                                new Trip { Time = "08:00", Bus = 10 },
                                new Trip { Time = "09:00", Bus = 20 }
                            }
                        }
                    }
                },
                new Stop
                {
                    Id = "ST002",
                    Name = "Main Square",
                    Schedule = new List<DaySchedule>()
                }
            }
        };
    }
}