#pragma warning disable CA2012 // NSubstitute Returns() on ValueTask causes false positive CA2012

using App.Shared.RCL.Services;

using FluentAssertions;

using Microsoft.JSInterop;

using NSubstitute;

namespace App.Shared.Tests;

public sealed class UserTimeZoneServiceTests
{

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; }
    }

    [Fact]
    public async Task InitializeAsync_sets_timezone_and_offset()
    {
        var js = Substitute.For<IJSRuntime>();
        js.InvokeAsync<string?>(Arg.Is("habitinatorGetUserTimezone"), Arg.Any<object?[]>())
            .Returns(new ValueTask<string?>("UTC"));
        js.InvokeAsync<int>(Arg.Is("habitinatorGetTimezoneOffsetMinutes"), Arg.Any<object?[]>())
            .Returns(new ValueTask<int>(0));

        var clock = new TestClock { UtcNow = new DateTimeOffset(2026, 5, 12, 12, 0, 0, TimeSpan.Zero) };
        var service = new UserTimeZoneService(js, clock);

        await service.InitializeAsync();

        service.TimeZoneId.Should().Be("UTC");
        service.UtcOffsetMinutes.Should().Be(0);
        service.IsDetected.Should().BeTrue();
    }

    [Fact]
    public async Task Conversions_are_identity_for_utc_timezone()
    {
        var js = Substitute.For<IJSRuntime>();
        js.InvokeAsync<string?>(Arg.Is("habitinatorGetUserTimezone"), Arg.Any<object?[]>())
            .Returns(new ValueTask<string?>("UTC"));
        js.InvokeAsync<int>(Arg.Is("habitinatorGetTimezoneOffsetMinutes"), Arg.Any<object?[]>())
            .Returns(new ValueTask<int>(0));

        var clock = new TestClock { UtcNow = new DateTimeOffset(2026, 5, 12, 8, 0, 0, TimeSpan.Zero) };
        var service = new UserTimeZoneService(js, clock);
        await service.InitializeAsync();

        var utc = new DateTimeOffset(2026, 5, 12, 9, 0, 0, TimeSpan.Zero);

        service.ConvertToLocal(utc).Should().Be(utc);
        service.ConvertToUtc(utc).Should().Be(utc);
        service.ConvertUtcTimeToLocal(TimeSpan.FromHours(10)).Should().Be(TimeSpan.FromHours(10));
        service.ConvertLocalTimeToUtc(TimeSpan.FromHours(10)).Should().Be(TimeSpan.FromHours(10));
    }

    [Fact]
    public void GetTimeZoneAbbreviation_returns_utc_when_not_initialized()
    {
        var js = Substitute.For<IJSRuntime>();
        var clock = new TestClock { UtcNow = new DateTimeOffset(2026, 5, 12, 0, 0, 0, TimeSpan.Zero) };
        var service = new UserTimeZoneService(js, clock);

        service.GetTimeZoneAbbreviation().Should().Be("UTC");
    }
}
