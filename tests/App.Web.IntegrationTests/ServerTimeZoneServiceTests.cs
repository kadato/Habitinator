using App.Web.Services;

using FluentAssertions;

namespace App.Web.IntegrationTests;

public sealed class ServerTimeZoneServiceTests
{
    [Fact]
    public void NoOverride_FallsBackToUtc()
    {
        var tz = new ServerTimeZoneService();
        tz.IsDetected.Should().BeFalse();
        tz.ConvertToLocal(new DateTimeOffset(2026, 4, 27, 0, 30, 0, TimeSpan.Zero))
            .Should().Be(new DateTimeOffset(2026, 4, 27, 0, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void ValidOverride_ResolvesTimeZone()
    {
        var tz = new ServerTimeZoneService();
        tz.SetOverride("Europe/Budapest");

        tz.IsDetected.Should().BeTrue();
        tz.TimeZoneId.Should().Be("Europe/Budapest");
        // 22:30 UTC on the 26th is 00:30 local on the 27th in summer, with a UTC+2 offset.
        tz.ConvertToLocal(new DateTimeOffset(2026, 4, 26, 22, 30, 0, TimeSpan.Zero))
            .Should().Be(new DateTimeOffset(2026, 4, 27, 0, 30, 0, TimeSpan.FromHours(2)));
    }

    [Fact]
    public void InvalidOverride_FallsBackToUtc()
    {
        var tz = new ServerTimeZoneService();
        tz.SetOverride("Not/AZone");

        tz.IsDetected.Should().BeFalse();
        tz.ConvertToLocal(new DateTimeOffset(2026, 4, 27, 0, 30, 0, TimeSpan.Zero))
            .Should().Be(new DateTimeOffset(2026, 4, 27, 0, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void ClearOverride_ReturnsToUtc()
    {
        var tz = new ServerTimeZoneService();
        tz.SetOverride("Europe/Budapest");
        tz.SetOverride(null);

        tz.IsDetected.Should().BeFalse();
    }

    [Fact]
    public void LocalToday_MatchesConvertToLocal()
    {
        var tz = new ServerTimeZoneService();
        tz.SetOverride("America/New_York");

        var localNow = tz.ConvertToLocal(DateTimeOffset.UtcNow);
        tz.LocalToday.Should().Be(DateOnly.FromDateTime(localNow.DateTime));
    }
}
