using App.Shared.RCL.Services;

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
        Assert.True(FocusDurationInput.TryParse(raw, out TimeSpan? d));
        Assert.NotNull(d);
        Assert.Equal(TimeSpan.FromSeconds(expectedSec), d);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_Whitespace_YieldsNullAndValid(string raw)
    {
        Assert.True(FocusDurationInput.TryParse(raw, out TimeSpan? d));
        Assert.Null(d);
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("0:0:0")]
    [InlineData("0:0")]
    [InlineData("24:0:0")]
    [InlineData("0:0:0:0")]
    public void TryParse_Rejected(string raw)
    {
        Assert.False(FocusDurationInput.TryParse(raw, out _));
    }

    [Fact]
    public void FormatForField_And_FormatForAlertLabel_RoundTripExamples()
    {
        TimeSpan t = TimeSpan.FromSeconds(45);
        Assert.Equal("45s", FocusDurationInput.FormatForField(t));
        Assert.Equal("45s", FocusDurationInput.FormatForAlertLabel(t));

        t = TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(30);
        Assert.Equal("5m30s", FocusDurationInput.FormatForField(t));

        t = TimeSpan.FromHours(1) + TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1);
        Assert.Equal("1:01:01", FocusDurationInput.FormatForField(t));
    }
}
