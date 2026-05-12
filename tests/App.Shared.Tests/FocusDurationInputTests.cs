using App.Shared.RCL.Services;
using FluentAssertions;

namespace App.Shared.Tests;

public sealed class FocusDurationInputTests
{
    [Theory]
    [InlineData("25", 25 * 60)]
    [InlineData("1:30", 90 * 60)]
    [InlineData("0:5", 5 * 60)]
    [InlineData("0:0:30", 30)]
    [InlineData("0:1:0", 60)]
    [InlineData("1:0:0", 3600)]
    [InlineData("1:2:3", 3600 + 120 + 3)]
    [InlineData("90s", 90)]
    [InlineData("5m30s", 5 * 60 + 30)]
    [InlineData("1h1m1s", 3600 + 60 + 1)]
    [InlineData("1h2s", 3600 + 2)]
    [InlineData("1h20", 3600 + 20 * 60)]
    [InlineData("1h2m", 3600 + 120)]
    public void TryParse_AcceptedFormats_ProducesTotalSeconds(string raw, int expectedSec)
    {
        FocusDurationInput.TryParse(raw, out TimeSpan? d).Should().BeTrue();
        d.Should().Be(TimeSpan.FromSeconds(expectedSec));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_Whitespace_YieldsNullAndValid(string raw)
    {
        FocusDurationInput.TryParse(raw, out TimeSpan? d).Should().BeTrue();
        d.Should().BeNull();
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("0:0:0")]
    [InlineData("0:0")]
    [InlineData("24:0:0")]
    [InlineData("0:0:0:0")]
    public void TryParse_Rejected(string raw)
    {
        FocusDurationInput.TryParse(raw, out _).Should().BeFalse();
    }

    [Fact]
    public void FormatForField_And_FormatForAlertLabel_RoundTripExamples()
    {
        TimeSpan t = TimeSpan.FromSeconds(45);
        FocusDurationInput.FormatForField(t).Should().Be("45s");
        FocusDurationInput.FormatForAlertLabel(t).Should().Be("45s");

        t = TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(30);
        FocusDurationInput.FormatForField(t).Should().Be("5m30s");

        t = TimeSpan.FromHours(1) + TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1);
        FocusDurationInput.FormatForField(t).Should().Be("1:01:01");
    }
}
