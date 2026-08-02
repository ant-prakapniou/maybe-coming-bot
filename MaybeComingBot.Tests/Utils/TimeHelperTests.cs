using FluentAssertions;
using MaybeComingBot.Utils;
using Microsoft.VisualStudio.TestPlatform.Utilities.Helpers;
using Xunit;

namespace MaybeComingBot.Tests.Utils;

/// <summary>
/// Tests for <see cref="TimeHelper"/> class.
/// </summary>
public class TimeHelperTests
{
    // ============================================================
    // ToMinutes Tests
    // ============================================================

    [Theory]
    [InlineData("00:00", 0)]
    [InlineData("01:00", 60)]
    [InlineData("12:00", 720)]
    [InlineData("23:59", 1439)]
    [InlineData("24:00", 1440)]
    [InlineData("9:00", 540)]
    [InlineData("09:00", 540)]
    public void ToMinutes_ValidFormat_ReturnsMinutes(string time, int expected)
    {
        // Act
        var result = TimeHelper.ToMinutes(time);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    [InlineData("invalid")]
    [InlineData("25:00")]
    [InlineData("00:60")]
    [InlineData("12:61")]
    public void ToMinutes_InvalidFormat_ReturnsNull(string? time)
    {
        // Act
        var result = TimeHelper.ToMinutes(time!);

        // Assert
        result.Should().BeNull();
    }

    // ============================================================
    // ToMinutesSafe Tests
    // ============================================================

    [Fact]
    public void ToMinutesSafe_ValidTime_ReturnsMinutes()
    {
        // Act
        var result = TimeHelper.ToMinutesSafe("14:30");

        // Assert
        result.Should().Be(870); // 14*60 + 30
    }

    [Fact]
    public void ToMinutesSafe_InvalidTime_ReturnsDefaultValue()
    {
        // Act
        var result = TimeHelper.ToMinutesSafe("invalid", 999);

        // Assert
        result.Should().Be(999);
    }

    [Fact]
    public void ToMinutesSafe_NullTime_ReturnsDefaultValue()
    {
        // Act
        var result = TimeHelper.ToMinutesSafe(null!, 42);

        // Assert
        result.Should().Be(42);
    }

    // ============================================================
    // TryParseMinutes Tests
    // ============================================================

    [Fact]
    public void TryParseMinutes_ValidTime_ReturnsTrueAndMinutes()
    {
        // Act
        var result = TimeHelper.TryParseMinutes("08:30", out var minutes);

        // Assert
        result.Should().BeTrue();
        minutes.Should().Be(510); // 8*60 + 30
    }

    [Fact]
    public void TryParseMinutes_InvalidTime_ReturnsFalseAndZero()
    {
        // Act
        var result = TimeHelper.TryParseMinutes("invalid", out var minutes);

        // Assert
        result.Should().BeFalse();
        minutes.Should().Be(0);
    }

    // ============================================================
    // IsValidTime Tests
    // ============================================================

    [Theory]
    [InlineData("00:00", true)]
    [InlineData("12:00", true)]
    [InlineData("23:59", true)]
    [InlineData("24:00", true)]
    [InlineData("invalid", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidTime_ReturnsCorrectResult(string? time, bool expected)
    {
        // Act
        var result = TimeHelper.IsValidTime(time!);

        // Assert
        result.Should().Be(expected);
    }

    // ============================================================
    // FormatMinutes Tests
    // ============================================================

    [Theory]
    [InlineData(0, "00:00")]
    [InlineData(60, "01:00")]
    [InlineData(720, "12:00")]
    [InlineData(1439, "23:59")]
    [InlineData(1440, "24:00")]
    public void FormatMinutes_ReturnsCorrectFormat(int minutes, string expected)
    {
        // Act
        var result = TimeHelper.FormatMinutes(minutes);

        // Assert
        result.Should().Be(expected);
    }

    // ============================================================
    // FormatTimeSpan Tests
    // ============================================================

    [Theory]
    [InlineData(0, "NOW")]
    [InlineData(-5, "NOW")]
    [InlineData(30, "30m")]
    [InlineData(60, "1h")]
    [InlineData(90, "1h 30m")]
    [InlineData(1440, "1d")]
    [InlineData(1500, "1d 1h")]
    [InlineData(1560, "1d 2h")]
    [InlineData(1565, "1d 2h 5m")]
    public void FormatTimeSpan_ReturnsCorrectFormat(int minutes, string expected)
    {
        // Act
        var result = TimeHelper.FormatTimeSpan(minutes);

        // Assert
        result.Should().Be(expected);
    }

    // ============================================================
    // EscapeMarkdown Tests
    // ============================================================

    [Theory]
    [InlineData("Hello World", "Hello World")]
    [InlineData("Hello_World", "Hello\\_World")]
    [InlineData("*bold*", "\\*bold\\*")]
    [InlineData("[link](url)", "\\[link\\]\\(url\\)")]
    [InlineData("_italic_", "\\_italic\\_")]
    [InlineData("`code`", "\\`code\\`")]
    [InlineData("# header", "\\# header")]
    [InlineData("+ plus - minus = equal", "\\+ plus \\- minus \\= equal")]
    public void EscapeMarkdown_ReturnsEscapedText(string input, string expected)
    {
        // Act
        var result = TimeHelper.EscapeMarkdown(input);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void EscapeMarkdown_NullText_ReturnsEmptyString()
    {
        // Act
        var result = TimeHelper.EscapeMarkdown(null!);

        // Assert
        result.Should().Be(string.Empty);
    }

    [Fact]
    public void EscapeMarkdown_EmptyText_ReturnsEmptyString()
    {
        // Act
        var result = TimeHelper.EscapeMarkdown(string.Empty);

        // Assert
        result.Should().Be(string.Empty);
    }

    // ============================================================
    // IsTimeInRange Tests
    // ============================================================

    [Theory]
    [InlineData("12:00", "10:00", "14:00", true)]
    [InlineData("09:00", "10:00", "14:00", false)]
    [InlineData("15:00", "10:00", "14:00", false)]
    [InlineData("12:00", "12:00", "12:00", true)]
    [InlineData("invalid", "10:00", "14:00", false)]
    public void IsTimeInRange_ReturnsCorrectResult(string time, string start, string end, bool expected)
    {
        // Act
        var result = TimeHelper.IsTimeInRange(time, start, end);

        // Assert
        result.Should().Be(expected);
    }

    // ============================================================
    // GetTimeDifference Tests
    // ============================================================

    [Theory]
    [InlineData("10:00", "12:00", 120)]
    [InlineData("22:00", "02:00", 240)] // Wrap around midnight
    [InlineData("00:00", "00:00", 0)]
    [InlineData("invalid", "12:00", 0)]
    public void GetTimeDifference_ReturnsCorrectDifference(string from, string to, int expected)
    {
        // Act
        var result = TimeHelper.GetTimeDifference(from, to);

        // Assert
        result.Should().Be(expected);
    }
}