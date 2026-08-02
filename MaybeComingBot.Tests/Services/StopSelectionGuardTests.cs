using MaybeComingBot.Abstractions;
using MaybeComingBot.Models;
using MaybeComingBot.Services;
using MaybeComingBot.Utils;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using FluentAssertions;

namespace MaybeComingBot.Tests.Services;

/// <summary>
/// Tests for <see cref="StopSelectionGuard"/> class.
/// </summary>
public class StopSelectionGuardTests
{
    private readonly Mock<IScheduleStorage> _storageMock;
    private readonly Mock<IUserStateService> _userStateMock;
    private readonly Mock<ITelegramSender> _senderMock;
    private readonly StopSelectionGuard _guard;

    public StopSelectionGuardTests()
    {
        _storageMock = new Mock<IScheduleStorage>();
        _userStateMock = new Mock<IUserStateService>();
        _senderMock = new Mock<ITelegramSender>();

        _guard = new StopSelectionGuard(
            _storageMock.Object,
            _userStateMock.Object,
            _senderMock.Object,
            Mock.Of<ILogger<StopSelectionGuard>>());
    }

    // ============================================================
    // EnsureStopSelectedAsync Tests
    // ============================================================

    [Fact]
    public async Task EnsureStopSelectedAsync_ValidState_ReturnsStop()
    {
        // Arrange
        var userId = 123L;
        var chatId = 456L;
        var stopId = "ST001";
        var stop = new Stop
        {
            Id = stopId,
            Name = "Central Station",
            Schedule = new List<DaySchedule>
            {
                new() { Trips = new List<Trip> { new("10:00", 10) } }
            }
        };

        _userStateMock
            .Setup(x => x.TryGet(userId, out It.Ref<UserState?>.IsAny))
            .Returns((long id, out UserState? s) =>
            {
                s = new UserState { SelectedStopId = stopId };
                return true;
            });

        _storageMock.Setup(x => x.GetStop(stopId)).Returns(stop);
        _storageMock.Setup(x => x.GetAllStops()).Returns(new List<Stop> { stop });

        // Act
        var result = await _guard.EnsureStopSelectedAsync(chatId, userId);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(stopId);
    }

    [Fact]
    public async Task EnsureStopSelectedAsync_NoState_ReturnsNullAndSendsMessage()
    {
        // Arrange
        var userId = 123L;
        var chatId = 456L;

        _userStateMock
            .Setup(x => x.TryGet(userId, out It.Ref<UserState?>.IsAny))
            .Returns((long id, out UserState? s) =>
            {
                s = null;
                return false;
            });

        // Act
        var result = await _guard.EnsureStopSelectedAsync(chatId, userId);

        // Assert
        result.Should().BeNull();
        _senderMock.Verify(x => x.SendAsync(chatId, It.IsAny<string>(), null, null, default), Times.Once);
    }

    [Fact]
    public async Task EnsureStopSelectedAsync_NoStopSelected_ReturnsNullAndSendsMessage()
    {
        // Arrange
        var userId = 123L;
        var chatId = 456L;

        _userStateMock
            .Setup(x => x.TryGet(userId, out It.Ref<UserState?>.IsAny))
            .Returns((long id, out UserState? s) =>
            {
                s = new UserState { SelectedStopId = string.Empty };
                return true;
            });

        // Act
        var result = await _guard.EnsureStopSelectedAsync(chatId, userId);

        // Assert
        result.Should().BeNull();
        _senderMock.Verify(x => x.SendAsync(chatId, It.IsAny<string>(), null, null, default), Times.Once);
    }

    [Fact]
    public async Task EnsureStopSelectedAsync_StopNotFound_ReturnsNullAndClearsState()
    {
        // Arrange
        var userId = 123L;
        var chatId = 456L;
        var stopId = "ST001";

        _userStateMock
            .Setup(x => x.TryGet(userId, out It.Ref<UserState?>.IsAny))
            .Returns((long id, out UserState? s) =>
            {
                s = new UserState { SelectedStopId = stopId };
                return true;
            });

        _storageMock.Setup(x => x.GetStop(stopId)).Returns((Stop?)null);
        _storageMock.Setup(x => x.GetAllStops()).Returns(new List<Stop> { new() { Id = "ST002", Name = "Other Stop" } });

        // Act
        var result = await _guard.EnsureStopSelectedAsync(chatId, userId);

        // Assert
        result.Should().BeNull();
        _userStateMock.Verify(x => x.ClearStop(userId), Times.Once);
        _senderMock.Verify(x => x.SendAsync(chatId, It.IsAny<string>(), null, null, default), Times.Once);
    }

    [Fact]
    public async Task EnsureStopSelectedAsync_NoStopsInStorage_ReturnsNull()
    {
        // Arrange
        var userId = 123L;
        var chatId = 456L;
        var stopId = "ST001";

        _userStateMock
            .Setup(x => x.TryGet(userId, out It.Ref<UserState?>.IsAny))
            .Returns((long id, out UserState? s) =>
            {
                s = new UserState { SelectedStopId = stopId };
                return true;
            });

        _storageMock.Setup(x => x.GetStop(stopId)).Returns((Stop?)null);
        _storageMock.Setup(x => x.GetAllStops()).Returns(new List<Stop>());

        // Act
        var result = await _guard.EnsureStopSelectedAsync(chatId, userId);

        // Assert
        result.Should().BeNull();
        _senderMock.Verify(x => x.SendAsync(chatId, It.IsAny<string>(), null, null, default), Times.Once);
    }

    [Fact]
    public async Task EnsureStopSelectedAsync_InvalidUserId_ReturnsNull()
    {
        // Act
        var result = await _guard.EnsureStopSelectedAsync(456L, 0L);

        // Assert
        result.Should().BeNull();
        _senderMock.Verify(x => x.SendAsync(It.IsAny<long>(), It.IsAny<string>(), null, null, default), Times.Never);
    }

    [Fact]
    public async Task EnsureStopSelectedAsync_InvalidChatId_ReturnsNull()
    {
        // Act
        var result = await _guard.EnsureStopSelectedAsync(0L, 123L);

        // Assert
        result.Should().BeNull();
        _senderMock.Verify(x => x.SendAsync(It.IsAny<long>(), It.IsAny<string>(), null, null, default), Times.Never);
    }

    [Fact]
    public async Task EnsureStopSelectedAsync_StopHasNoSchedule_ReturnsNullAndSendsMessage()
    {
        // Arrange
        var userId = 123L;
        var chatId = 456L;
        var stopId = "ST001";
        var stop = new Stop { Id = stopId, Name = "Central Station" };

        _userStateMock
            .Setup(x => x.TryGet(userId, out It.Ref<UserState?>.IsAny))
            .Returns((long id, out UserState? s) =>
            {
                s = new UserState { SelectedStopId = stopId };
                return true;
            });

        _storageMock.Setup(x => x.GetStop(stopId)).Returns(stop);

        // Act
        var result = await _guard.EnsureStopSelectedAsync(chatId, userId);

        // Assert
        result.Should().BeNull();
        _senderMock.Verify(x => x.SendAsync(chatId, It.IsAny<string>(), null, null, default), Times.Once);
    }
}

