using MaybeComingBot.Models;
using MaybeComingBot.Utils;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace MaybeComingBot.Tests.Utils;

/// <summary>
/// Tests for <see cref="RateLimiter"/> class.
/// </summary>
public class RateLimiterTests
{
    private readonly RateLimiter _rateLimiter;
    private readonly BotSettings _settings;
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero));

    public RateLimiterTests()
    {
        _settings = new BotSettings
        {
            RateLimitMaxRequests = 3,
            RateLimitWindowMinutes = 1,
            AdminUserIds = new long[] { 1, 2 }
        };

        var optionsMock = new Mock<IOptions<BotSettings>>();
        optionsMock.Setup(x => x.Value).Returns(_settings);

        var loggerMock = new Mock<ILogger<RateLimiter>>();

        _rateLimiter = new RateLimiter(optionsMock.Object, loggerMock.Object, _time);
    }

    // ============================================================
    // IsAllowedAsync Tests
    // ============================================================

    [Fact]
    public async Task IsAllowedAsync_AdminUser_AlwaysAllowed()
    {
        // Arrange
        var adminId = 1L;

        // Act
        var result1 = await _rateLimiter.IsAllowedAsync(adminId);
        var result2 = await _rateLimiter.IsAllowedAsync(adminId);
        var result3 = await _rateLimiter.IsAllowedAsync(adminId);
        var result4 = await _rateLimiter.IsAllowedAsync(adminId);

        // Assert
        result1.Should().BeTrue();
        result2.Should().BeTrue();
        result3.Should().BeTrue();
        result4.Should().BeTrue();
    }

    [Fact]
    public async Task IsAllowedAsync_RegularUser_WithinLimit_ReturnsTrue()
    {
        // Arrange
        var userId = 100L;

        // Act
        var result1 = await _rateLimiter.IsAllowedAsync(userId);
        var result2 = await _rateLimiter.IsAllowedAsync(userId);
        var result3 = await _rateLimiter.IsAllowedAsync(userId);

        // Assert
        result1.Should().BeTrue();
        result2.Should().BeTrue();
        result3.Should().BeTrue();
    }

    [Fact]
    public async Task IsAllowedAsync_RegularUser_ExceedsLimit_ReturnsFalse()
    {
        // Arrange
        var userId = 100L;
        await _rateLimiter.IsAllowedAsync(userId);
        await _rateLimiter.IsAllowedAsync(userId);
        await _rateLimiter.IsAllowedAsync(userId);

        // Act
        var result = await _rateLimiter.IsAllowedAsync(userId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsAllowedAsync_RegularUser_AfterWindowReset_ReturnsTrue()
    {
        // Arrange
        var userId = 100L;
        var maxRequests = _settings.RateLimitMaxRequests;

        // Достигаем лимита
        for (int i = 0; i < maxRequests; i++)
        {
            var result = await _rateLimiter.IsAllowedAsync(userId);
            result.Should().BeTrue();
        }

        // Проверяем, что лимит достигнут
        var exceeded = await _rateLimiter.IsAllowedAsync(userId);
        exceeded.Should().BeFalse();

        // Act - advance the fake clock past the window instead of sleeping.
        _time.Advance(TimeSpan.FromMinutes(_settings.RateLimitWindowMinutes + 0.1));

        var resultAfterReset = await _rateLimiter.IsAllowedAsync(userId);

        // Assert
        resultAfterReset.Should().BeTrue();
    }

    [Fact]
    public async Task IsAllowedAsync_DifferentUsers_IndependentLimits()
    {
        // Arrange
        var user1 = 100L;
        var user2 = 200L;

        // Act
        await _rateLimiter.IsAllowedAsync(user1);
        await _rateLimiter.IsAllowedAsync(user1);
        await _rateLimiter.IsAllowedAsync(user1);
        var result1 = await _rateLimiter.IsAllowedAsync(user1);

        var result2 = await _rateLimiter.IsAllowedAsync(user2);

        // Assert
        result1.Should().BeFalse();
        result2.Should().BeTrue();
    }

    // ============================================================
    // TotalRateLimitExceeded Tests
    // ============================================================

    [Fact]
    public async Task TotalRateLimitExceeded_WhenUserExceeds_ReturnsCorrectCount()
    {
        // Arrange
        var userId = 100L;
        await _rateLimiter.IsAllowedAsync(userId);
        await _rateLimiter.IsAllowedAsync(userId);
        await _rateLimiter.IsAllowedAsync(userId);
        await _rateLimiter.IsAllowedAsync(userId); // Превышение

        // Act
        var total = _rateLimiter.TotalRateLimitExceeded;

        // Assert
        total.Should().Be(1);
    }

    [Fact]
    public async Task TotalRateLimitExceeded_MultipleExceedances_ReturnsCorrectCount()
    {
        // Arrange
        var user1 = 100L;
        var user2 = 200L;

        await _rateLimiter.IsAllowedAsync(user1);
        await _rateLimiter.IsAllowedAsync(user1);
        await _rateLimiter.IsAllowedAsync(user1);
        await _rateLimiter.IsAllowedAsync(user1); // Превышение

        await _rateLimiter.IsAllowedAsync(user2);
        await _rateLimiter.IsAllowedAsync(user2);
        await _rateLimiter.IsAllowedAsync(user2);
        await _rateLimiter.IsAllowedAsync(user2); // Превышение

        // Act
        var total = _rateLimiter.TotalRateLimitExceeded;

        // Assert
        total.Should().Be(2);
    }

    // ============================================================
    // ActiveUsersCount Tests
    // ============================================================

    [Fact]
    public async Task ActiveUsersCount_ReturnsCorrectCount()
    {
        // Arrange
        await _rateLimiter.IsAllowedAsync(100L);
        await _rateLimiter.IsAllowedAsync(200L);
        await _rateLimiter.IsAllowedAsync(300L);

        // Act
        var count = _rateLimiter.ActiveUsersCount;

        // Assert
        count.Should().Be(3);
    }

    [Fact]
    public async Task ActiveUsersCount_AdminsNotTracked()
    {
        // Arrange
        await _rateLimiter.IsAllowedAsync(1L); // Admin
        await _rateLimiter.IsAllowedAsync(100L); // Regular

        // Act
        var count = _rateLimiter.ActiveUsersCount;

        // Assert
        count.Should().Be(1);
    }

    // ============================================================
    // ResetUser Tests
    // ============================================================

    [Fact]
    public async Task ResetUser_ClearsRateLimitForSpecificUser()
    {
        // Arrange
        var userId = 100L;
        await _rateLimiter.IsAllowedAsync(userId);
        await _rateLimiter.IsAllowedAsync(userId);
        await _rateLimiter.IsAllowedAsync(userId);
        var beforeReset = await _rateLimiter.IsAllowedAsync(userId);
        beforeReset.Should().BeFalse();

        // Act
        _rateLimiter.ResetUser(userId);
        var afterReset = await _rateLimiter.IsAllowedAsync(userId);

        // Assert
        afterReset.Should().BeTrue();
    }

    [Fact]
    public void ResetUser_NonExistingUser_DoesNothing()
    {
        // Act
        var act = () => _rateLimiter.ResetUser(999);

        // Assert
        act.Should().NotThrow();
    }

    // ============================================================
    // Dispose Tests
    // ============================================================

    [Fact]
    public void Dispose_CalledMultipleTimes_DoesNotThrow()
    {
        // Act
        var act = () =>
        {
            _rateLimiter.Dispose();
            _rateLimiter.Dispose();
        };

        // Assert
        act.Should().NotThrow();
    }
}