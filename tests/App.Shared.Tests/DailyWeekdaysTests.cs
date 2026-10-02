using App.Shared.RCL.Models;

using FluentAssertions;

namespace App.Shared.Tests;

public sealed class DailyWeekdaysTests
{
    [Fact]
    public void Normalize_ClampsToSevenBits()
    {
        DailyWeekdays.Normalize(255).Should().Be(127);
        DailyWeekdays.Normalize(-1).Should().Be(127);
        DailyWeekdays.Normalize(0).Should().Be(0);
        DailyWeekdays.Normalize(null).Should().Be(0);
    }

    [Fact]
    public void RoundTrip_FromAndToDays()
    {
        var mask = DailyWeekdays.From(DayOfWeek.Monday, DayOfWeek.Friday);
        DailyWeekdays.Has(mask, DayOfWeek.Monday).Should().BeTrue();
        DailyWeekdays.Has(mask, DayOfWeek.Tuesday).Should().BeFalse();
        DailyWeekdays.ToDays(mask).Should().BeEquivalentTo([DayOfWeek.Monday, DayOfWeek.Friday]);
        DailyWeekdays.HasAny(mask).Should().BeTrue();
        DailyWeekdays.HasAny(0).Should().BeFalse();
    }
}
