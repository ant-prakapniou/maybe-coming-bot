using MaybeComingBot.Abstractions;
using MaybeComingBot.Models;
using MaybeComingBot.Services;
using MaybeComingBot.Utils;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace MaybeComingBot.Tests.Services;

public class ScheduleRefreshServiceTests
{
    private readonly Mock<ScheduleLoader> _loaderMock;
    private readonly Mock<IScheduleStorage> _storageMock;
    private readonly Mock<ILogger<ScheduleRefreshService>> _loggerMock;
    private readonly BotSettings _settings;
    private readonly IOptions<BotSettings> _options;

    public ScheduleRefreshServiceTests()
    {
        _loaderMock = new Mock<ScheduleLoader>(
            Mock.Of<IHttpClientFactory>(),
            Mock.Of<ILogger<ScheduleLoader>>())
        {
            CallBase = true
        };

        _storageMock = new Mock<IScheduleStorage>();
        _loggerMock = new Mock<ILogger<ScheduleRefreshService>>();

        _settings = new BotSettings
        {
            UpdateIntervalHours = 24,
            UserStateTtlHours = 48
        };

        _options = Options.Create(_settings);
    }

    // ============================================================
    // Helper Methods
    // ============================================================

    private static ScheduleRoot CreateValidSchedule()
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
                }
            }
        };
    }

    // ============================================================
    // ForceRefreshAsync Tests
    // ============================================================

    [Fact]
    public async Task ForceRefreshAsync_LoadsAndUpdatesSchedule()
    {
        // Arrange
        var service = new ScheduleRefreshService(
            _loaderMock.Object,
            _storageMock.Object,
            _options,
            _loggerMock.Object);

        var schedule = CreateValidSchedule();
        _loaderMock.Setup(x => x.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(schedule);

        _storageMock.Setup(x => x.HasData()).Returns(true);

        // Act
        await service.ForceRefreshAsync(TestContext.Current.CancellationToken);

        // Assert
        _loaderMock.Verify(x => x.LoadAsync(It.IsAny<CancellationToken>()), Times.Once);
        _storageMock.Verify(x => x.Update(schedule), Times.Once);
    }

    [Fact]
    public async Task ForceRefreshAsync_WhenRefreshFails_IncrementsFailureCount()
    {
        // Arrange
        var service = new ScheduleRefreshService(
            _loaderMock.Object,
            _storageMock.Object,
            _options,
            _loggerMock.Object);

        _loaderMock.Setup(x => x.LoadAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Network error"));

        _storageMock.Setup(x => x.HasData()).Returns(true);

        // Act
        try
        {
            await service.ForceRefreshAsync(TestContext.Current.CancellationToken);
        }
        catch
        {
            // Expected
        }

        // Assert
        service.FailureCount.Should().Be(1);
    }

    [Fact]
    public async Task ForceRefreshAsync_AfterSuccess_ResetsFailureCount()
    {
        // Arrange
        var service = new ScheduleRefreshService(
            _loaderMock.Object,
            _storageMock.Object,
            _options,
            _loggerMock.Object);

        var schedule = CreateValidSchedule();

        _loaderMock.SetupSequence(x => x.LoadAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Network error"))
            .ReturnsAsync(schedule);

        _storageMock.Setup(x => x.HasData()).Returns(true);

        // Act - первый вызов падает
        try
        {
            await service.ForceRefreshAsync(TestContext.Current.CancellationToken);
        }
        catch
        {
            // Expected
        }

        service.FailureCount.Should().Be(1);

        // Act - второй вызов успешен
        await service.ForceRefreshAsync(TestContext.Current.CancellationToken);

        // Assert
        service.FailureCount.Should().Be(0);
    }

    [Fact]
    public async Task ForceRefreshAsync_MultipleFailures_IncrementsCount()
    {
        // Arrange
        var service = new ScheduleRefreshService(
            _loaderMock.Object,
            _storageMock.Object,
            _options,
            _loggerMock.Object);

        _loaderMock.Setup(x => x.LoadAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Network error"));

        _storageMock.Setup(x => x.HasData()).Returns(true);

        // Act
        for (int i = 0; i < 3; i++)
        {
            try
            {
                await service.ForceRefreshAsync(TestContext.Current.CancellationToken);
            }
            catch
            {
                // Expected
            }
        }

        // Assert
        service.FailureCount.Should().Be(3);
    }

    // ============================================================
    // GetStatus Tests
    // ============================================================

    [Fact]
    public void GetStatus_WhenNotLoaded_ReturnsNotLoaded()
    {
        // Arrange
        var service = new ScheduleRefreshService(
            _loaderMock.Object,
            _storageMock.Object,
            _options,
            _loggerMock.Object);

        // Act
        var status = service.GetStatus();

        // Assert
        status.Should().Be("NOT LOADED");
    }

    [Fact]
    public async Task GetStatus_AfterSuccess_ReturnsSuccessStatus()
    {
        // Arrange
        var service = new ScheduleRefreshService(
            _loaderMock.Object,
            _storageMock.Object,
            _options,
            _loggerMock.Object);

        var schedule = CreateValidSchedule();
        _loaderMock.Setup(x => x.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(schedule);

        _storageMock.Setup(x => x.HasData()).Returns(true);

        // Act
        await service.ForceRefreshAsync(TestContext.Current.CancellationToken);
        var status = service.GetStatus();

        // Assert
        status.Should().StartWith("✅ LAST:");
    }

    // ============================================================
    // GetLastAttemptInfo Tests
    // ============================================================

    [Fact]
    public void GetLastAttemptInfo_WhenNoAttempts_ReturnsNoAttempts()
    {
        // Arrange
        var service = new ScheduleRefreshService(
            _loaderMock.Object,
            _storageMock.Object,
            _options,
            _loggerMock.Object);

        // Act
        var info = service.GetLastAttemptInfo();

        // Assert
        info.Should().Be("NO ATTEMPTS");
    }

    [Fact]
    public async Task GetLastAttemptInfo_AfterAttempt_ReturnsAttemptInfo()
    {
        // Arrange
        var service = new ScheduleRefreshService(
            _loaderMock.Object,
            _storageMock.Object,
            _options,
            _loggerMock.Object);

        var schedule = CreateValidSchedule();
        _loaderMock.Setup(x => x.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(schedule);

        _storageMock.Setup(x => x.HasData()).Returns(true);

        // Act
        await service.ForceRefreshAsync(TestContext.Current.CancellationToken);
        var info = service.GetLastAttemptInfo();

        // Assert
        info.Should().StartWith("ATTEMPT:");
        info.Should().Contain("FAILS: 0");
    }

    [Fact]
    public async Task GetLastAttemptInfo_AfterFailedAttempt_ShowsFailureCount()
    {
        // Arrange
        var service = new ScheduleRefreshService(
            _loaderMock.Object,
            _storageMock.Object,
            _options,
            _loggerMock.Object);

        _loaderMock.Setup(x => x.LoadAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Network error"));

        _storageMock.Setup(x => x.HasData()).Returns(true);

        // Act
        try
        {
            await service.ForceRefreshAsync(TestContext.Current.CancellationToken);
        }
        catch
        {
            // Expected
        }

        var info = service.GetLastAttemptInfo();

        // Assert
        info.Should().Contain("FAILS: 1");
    }

    // ============================================================
    // Dispose Tests
    // ============================================================

    [Fact]
    public void Dispose_ReleasesResources()
    {
        // Arrange
        var service = new ScheduleRefreshService(
            _loaderMock.Object,
            _storageMock.Object,
            _options,
            _loggerMock.Object);

        // Act
        var act = () => service.Dispose();

        // Assert
        act.Should().NotThrow();
    }
}