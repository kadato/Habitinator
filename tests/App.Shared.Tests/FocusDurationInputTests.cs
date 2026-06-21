using App.Shared.RCL.Services;

using FluentAssertions;

namespace App.Shared.Tests;

public sealed class FocusDurationInputTests
{
    [Theory]
    // TryParsePlainMinutes
    [InlineData("25", 25 * 60)]
    [InlineData(" 25 ", 25 * 60)]
    // TryParseSecondsSuffix
    [InlineData("90s", 90)]
    [InlineData("90S", 90)]
    [InlineData("90 s", 90)]
    // TryParseMinutesSeconds
    [InlineData("5m30s", 5 * 60 + 30)]
    [InlineData("5M30S", 5 * 60 + 30)]
    [InlineData("5 m 30 s", 5 * 60 + 30)]
    // TryParseThreePartColon
    [InlineData("0:0:30", 30)]
    [InlineData("0:1:0", 60)]
    [InlineData("1:0:0", 3600)]
    [InlineData("1:2:3", 3600 + 120 + 3)]
    // TryParseTwoPartColon
    [InlineData("1:30", 90 * 60)]
    [InlineData("0:5", 5 * 60)]
    // TryParseMinutesOnlySuffix
    [InlineData("25m", 25 * 60)]
    [InlineData("25M", 25 * 60)]
    [InlineData("25 m", 25 * 60)]
    // TryParseHmsLongForm
    [InlineData("1h1m1s", 3600 + 60 + 1)]
    [InlineData("1H1M1S", 3600 + 60 + 1)]
    [InlineData("1h2s", 3600 + 2)]
    [InlineData("1h2m", 3600 + 120)]
    // TryParseHMSuffixForm
    [InlineData("1h20", 3600 + 20 * 60)]
    [InlineData("1h20m", 3600 + 20 * 60)]
    [InlineData("1h", 3600)]
    [InlineData("1 H", 3600)]
    public void TryParse_ValidFormats_ProducesExpectedTimeSpan(string raw, int expectedSeconds)
    {
        FocusDurationInput.TryParse(raw, out TimeSpan? d).Should().BeTrue();
        d.Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TryParse_WhitespaceOrNull_YieldsNullAndValid(string? raw)
    {
        FocusDurationInput.TryParse(raw, out TimeSpan? d).Should().BeTrue();
        d.Should().BeNull();
    }

    [Theory]
    // Zero values
    [InlineData("0")]
    [InlineData("0s")]
    [InlineData("0m")]
    [InlineData("0h")]
    [InlineData("0:0")]
    [InlineData("0:0:0")]
    // Negative values
    [InlineData("-5")]
    [InlineData("-5s")]
    [InlineData("-1:30")]
    // Exceeding limit
    [InlineData("24:00:00")]
    [InlineData("24h")]
    [InlineData("25h")]
    [InlineData("1440")] // 24 hours in minutes
    [InlineData("86400s")]
    [InlineData("24h1m")]
    // Invalid ranges
    [InlineData("1:60")] // minutes > 59
    [InlineData("0:0:60")] // seconds > 59
    [InlineData("5m60s")] // seconds > 59
    [InlineData("25h5m")] // hours > 23 in long forms
    // Malformed strings
    [InlineData("abc")]
    [InlineData("25a")]
    [InlineData("  abc  ")]
    [InlineData("1.5")]
    [InlineData("1::2")]
    [InlineData("1:2:")]
    [InlineData(":1:2")]
    [InlineData("hms")]
    // Overflow attempts
    [InlineData("9999999999")]
    [InlineData("9999999999s")]
    [InlineData("9999999999m")]
    [InlineData("9999999999h")]
    [InlineData("9999999999:00")]
    [InlineData("0:9999999999")]
    [InlineData("9999999999:00:00")]
    [InlineData("0:9999999999:00")]
    [InlineData("0:0:9999999999")]
    public void TryParse_InvalidOrOutofBounds_ReturnsFalseAndDoesNotThrow(string raw)
    {
        FocusDurationInput.TryParse(raw, out _).Should().BeFalse();
    }

    [Theory]
    // Negative or zero timespans -> "0s"
    [InlineData(0, "0s")]
    [InlineData(-10, "0s")]
    // Under 60 seconds
    [InlineData(1, "1s")]
    [InlineData(15, "15s")]
    [InlineData(59, "59s")]
    public void FormatForAlertLabel_SubMinute_ProducesExpected(double seconds, string expected)
    {
        FocusDurationInput.FormatForAlertLabel(TimeSpan.FromSeconds(seconds)).Should().Be(expected);
    }

    [Theory]
    // Round minutes under 60 minutes
    [InlineData(60, "1 min")]
    [InlineData(5 * 60, "5 min")]
    [InlineData(59 * 60, "59 min")]
    // Round hours
    [InlineData(60 * 60, "1h")]
    [InlineData(5 * 60 * 60, "5h")]
    // Hours and minutes (no seconds)
    [InlineData(60 * 60 + 15 * 60, "1h 15m")]
    [InlineData(23 * 60 * 60 + 59 * 60, "23h 59m")]
    // Minutes and seconds (no hours)
    [InlineData(5 * 60 + 30, "5m 30s")]
    [InlineData(59 * 60 + 59, "59m 59s")]
    // Hours and seconds (no minutes)
    [InlineData(60 * 60 + 2, "1h 0m 2s")]
    [InlineData(23 * 60 * 60 + 59, "23h 0m 59s")]
    // Hours, minutes, and seconds
    [InlineData(3600 + 120 + 3, "1h 2m 3s")]
    [InlineData(23 * 3600 + 59 * 60 + 59, "23h 59m 59s")]
    public void FormatForAlertLabel_ProducesExpected(int seconds, string expected)
    {
        FocusDurationInput.FormatForAlertLabel(TimeSpan.FromSeconds(seconds)).Should().Be(expected);
    }

    [Fact]
    public void FormatForAlertLabel_CeilingHandling()
    {
        FocusDurationInput.FormatForAlertLabel(TimeSpan.FromMilliseconds(500)).Should().Be("1s");
        FocusDurationInput.FormatForAlertLabel(TimeSpan.FromMilliseconds(5500)).Should().Be("6s");
        FocusDurationInput.FormatForAlertLabel(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1)).Should().Be("5m 1s");
    }

    [Fact]
    public void FormatForField_NullOrZeroOrNegative_ReturnsEmpty()
    {
        FocusDurationInput.FormatForField(null).Should().Be(string.Empty);
        FocusDurationInput.FormatForField(TimeSpan.Zero).Should().Be(string.Empty);
        FocusDurationInput.FormatForField(TimeSpan.FromSeconds(-5)).Should().Be(string.Empty);
    }

    [Theory]
    // Seconds only (h == 0, m == 0)
    [InlineData(45, "45s")]
    [InlineData(59, "59s")]
    // Minutes only (h == 0, s == 0) -> "m"
    [InlineData(60, "1")]
    [InlineData(25 * 60, "25")]
    // Minutes and seconds (h == 0, m > 0, s > 0) -> "m m s s"
    [InlineData(5 * 60 + 30, "5m30s")]
    // Hours only (m == 0, s == 0) -> "h h"
    [InlineData(3600, "1h")]
    [InlineData(5 * 3600, "5h")]
    // Hours and minutes (s == 0) -> "h:mm"
    [InlineData(3600 + 15 * 60, "1:15")]
    [InlineData(3600 + 5 * 60, "1:05")]
    // Hours, minutes, and seconds -> "h:mm:ss"
    [InlineData(3600 + 120 + 3, "1:02:03")]
    [InlineData(23 * 3600 + 59 * 60 + 59, "23:59:59")]
    public void FormatForField_ProducesExpected(int seconds, string expected)
    {
        FocusDurationInput.FormatForField(TimeSpan.FromSeconds(seconds)).Should().Be(expected);
    }

    [Fact]
    public void FormatForField_ClampsToMaxFocusDuration()
    {
        // 25 hours is clamped to 23:59:59
        FocusDurationInput.FormatForField(TimeSpan.FromHours(25)).Should().Be("23:59:59");
    }
}
