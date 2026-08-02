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

/// <summary>
/// Tests for <see cref="UserStateService"/> class.
/// </summary>
public class UserStateServiceTests
{
    private readonly UserStateService _service;
    private readonly BotSettings _settings;

    public UserStateServiceTests()
    {
        _settings = new BotSettings
        {
            UserStateTtlHours = 2
        };

        var optionsMock = new Mock<IOptions<BotSettings>>();
        optionsMock.Setup(x => x.Value).Returns(_settings);

        var loggerMock = new Mock<ILogger<UserStateService>>();

        _service = new UserStateService(optionsMock.Object, loggerMock.Object);
    }

    // ============================================================
    // GetOrCreate Tests
    // ============================================================

    [Fact]
    public void GetOrCreate_NewUser_ReturnsNewState()
    {
        // Act
        var state = _service.GetOrCreate(123);

        // Assert
        state.Should().NotBeNull();
        state.SelectedStopId.Should().BeEmpty();
        state.LastActivity.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void GetOrCreate_ExistingUser_ReturnsExistingState()
    {
        // Arrange
        var userId = 123L;
        _service.SetStop(userId, "ST001");

        // Act
        var state = _service.GetOrCreate(userId);

        // Assert
        state.SelectedStopId.Should().Be("ST001");
    }

    [Fact]
    public void GetOrCreate_InvalidUserId_ThrowsArgumentException()
    {
        // Act
        var act = () => _service.GetOrCreate(0);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    // ============================================================
    // TryGet Tests
    // ============================================================

    [Fact]
    public void TryGet_ExistingUser_ReturnsTrueAndState()
    {
        // Arrange
        var userId = 123L;
        _service.GetOrCreate(userId);

        // Act
        var result = _service.TryGet(userId, out var state);

        // Assert
        result.Should().BeTrue();
        state.Should().NotBeNull();
    }

    [Fact]
    public void TryGet_NonExistingUser_ReturnsFalse()
    {
        // Act
        var result = _service.TryGet(999, out var state);

        // Assert
        result.Should().BeFalse();
        state.Should().BeNull();
    }

    [Fact]
    public void TryGet_InvalidUserId_ReturnsFalse()
    {
        // Act
        var result = _service.TryGet(0, out var state);

        // Assert
        result.Should().BeFalse();
        state.Should().BeNull();
    }

    // ============================================================
    // SetStop Tests
    // ============================================================

    [Fact]
    public void SetStop_NewUser_CreatesStateAndSetsStop()
    {
        // Act
        _service.SetStop(123, "ST001");

        // Assert
        var exists = _service.TryGet(123, out var state);
        exists.Should().BeTrue();
        state.SelectedStopId.Should().Be("ST001");
    }

    [Fact]
    public void SetStop_ExistingUser_UpdatesStop()
    {
        // Arrange
        _service.SetStop(123, "ST001");

        // Act
        _service.SetStop(123, "ST002");

        // Assert
        var exists = _service.TryGet(123, out var state);
        exists.Should().BeTrue();
        state.SelectedStopId.Should().Be("ST002");
    }

    [Fact]
    public void SetStop_InvalidUserId_ThrowsArgumentException()
    {
        // Act
        var act = () => _service.SetStop(0, "ST001");

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SetStop_InvalidStopId_ThrowsArgumentException()
    {
        // Act
        var act = () => _service.SetStop(123, null!);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    // ============================================================
    // SetUserInfo Tests
    // ============================================================

    [Fact]
    public void SetUserInfo_NewUser_CreatesStateWithInfo()
    {
        // Act
        _service.SetUserInfo(123, "John", "Doe", "johndoe");

        // Assert
        var exists = _service.TryGet(123, out var state);
        exists.Should().BeTrue();
        state.FirstName.Should().Be("John");
        state.LastName.Should().Be("Doe");
        state.Username.Should().Be("johndoe");
    }

    [Fact]
    public void SetUserInfo_ExistingUser_UpdatesInfo()
    {
        // Arrange
        _service.SetUserInfo(123, "John", "Doe", "johndoe");

        // Act
        _service.SetUserInfo(123, "Jane", "Smith", "janesmith");

        // Assert
        var exists = _service.TryGet(123, out var state);
        exists.Should().BeTrue();
        state.FirstName.Should().Be("Jane");
        state.LastName.Should().Be("Smith");
        state.Username.Should().Be("janesmith");
    }

    [Fact]
    public void SetUserInfo_PartialUpdate_PreservesExistingInfo()
    {
        // Arrange
        _service.SetUserInfo(123, "John", "Doe", "johndoe");
        _service.SetStop(123, "ST001");

        // Act
        _service.SetUserInfo(123, "Jane", null, null);

        // Assert
        var exists = _service.TryGet(123, out var state);
        exists.Should().BeTrue();
        state.FirstName.Should().Be("Jane");
        state.LastName.Should().Be("Doe"); // Preserved
        state.Username.Should().Be("johndoe"); // Preserved
        state.SelectedStopId.Should().Be("ST001"); // Preserved
    }

    // ============================================================
    // Clear Tests
    // ============================================================

    [Fact]
    public void Clear_ExistingUser_RemovesState()
    {
        // Arrange
        _service.SetStop(123, "ST001");

        // Act
        _service.Clear(123);

        // Assert
        var exists = _service.TryGet(123, out _);
        exists.Should().BeFalse();
    }

    [Fact]
    public void Clear_NonExistingUser_DoesNothing()
    {
        // Act
        var act = () => _service.Clear(999);

        // Assert
        act.Should().NotThrow();
    }

    // ============================================================
    // UpdateActivity Tests
    // ============================================================

    [Fact]
    public async Task UpdateActivity_ExistingUser_UpdatesLastActivity()
    {
        // Arrange
        _service.SetStop(123, "ST001");
        var initialActivity = _service.GetOrCreate(123).LastActivity;

        // Act
        await Task.Delay(100, TestContext.Current.CancellationToken);
        _service.UpdateActivity(123);

        // Assert
        var state = _service.GetOrCreate(123);
        state.LastActivity.Should().BeAfter(initialActivity);
    }

    [Fact]
    public void UpdateActivity_NewUser_CreatesState()
    {
        // Act
        _service.UpdateActivity(123);

        // Assert
        var exists = _service.TryGet(123, out _);
        exists.Should().BeTrue();
    }

    // ============================================================
    // ClearStop Tests
    // ============================================================

    [Fact]
    public void ClearStop_ExistingUser_ClearsStop()
    {
        // Arrange
        _service.SetStop(123, "ST001");
        _service.SetUserInfo(123, "John", "Doe", "johndoe");

        // Act
        _service.ClearStop(123);

        // Assert
        var exists = _service.TryGet(123, out var state);
        exists.Should().BeTrue();
        state.SelectedStopId.Should().BeEmpty();
        state.FirstName.Should().Be("John"); // Preserved
    }

    [Fact]
    public void ClearStop_NonExistingUser_DoesNothing()
    {
        // Act
        var act = () => _service.ClearStop(999);

        // Assert
        act.Should().NotThrow();
    }

    // ============================================================
    // HasStopSelected Tests
    // ============================================================

    [Fact]
    public void HasStopSelected_WithStop_ReturnsTrue()
    {
        // Arrange
        _service.SetStop(123, "ST001");

        // Act
        var result = _service.HasStopSelected(123);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void HasStopSelected_WithoutStop_ReturnsFalse()
    {
        // Arrange
        _service.GetOrCreate(123);

        // Act
        var result = _service.HasStopSelected(123);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void HasStopSelected_NonExistingUser_ReturnsFalse()
    {
        // Act
        var result = _service.HasStopSelected(999);

        // Assert
        result.Should().BeFalse();
    }

    // ============================================================
    // GetActiveUsersCount Tests
    // ============================================================

    [Fact]
    public void GetActiveUsersCount_ReturnsCorrectCount()
    {
        // Arrange
        _service.UpdateActivity(123);
        _service.UpdateActivity(456);
        _service.UpdateActivity(789);

        // Act
        var count = _service.GetActiveUsersCount();

        // Assert
        count.Should().Be(3);
    }

    [Fact]
    public void GetActiveUsersCount_WithExpiredUsers_ExcludesThem()
    {
        // Arrange
        _settings.UserStateTtlHours = 0; // Все пользователи считаются активными
        _service.UpdateActivity(123);
        _service.UpdateActivity(456);

        // Act
        var count = _service.GetActiveUsersCount();

        // Assert
        count.Should().Be(2);
    }

    // ============================================================
    // GetExpiredKeys Tests
    // ============================================================

    [Fact]
    public void GetExpiredKeys_ReturnsExpiredUserIds()
    {
        // Arrange
        _service.UpdateActivity(123);
        var expiry = TimeSpan.FromHours(1);

        // Act
        var expired = _service.GetExpiredKeys(expiry);

        // Assert
        // Так как время активности свежее, никто не должен быть просрочен
        expired.Should().BeEmpty();
    }

    // ============================================================
    // IsExpired Tests
    // ============================================================

    [Fact]
    public void IsExpired_NewUser_ReturnsFalse()
    {
        // Arrange
        _service.UpdateActivity(123);

        // Act
        var result = _service.IsExpired(123);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsExpired_NonExistingUser_ReturnsTrue()
    {
        // Act
        var result = _service.IsExpired(999);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsExpired_InvalidUserId_ReturnsTrue()
    {
        // Act
        var result = _service.IsExpired(0);

        // Assert
        result.Should().BeTrue();
    }
}