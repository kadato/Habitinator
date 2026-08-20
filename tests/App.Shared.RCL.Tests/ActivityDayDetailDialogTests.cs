#pragma warning disable MUD0012

using App.Shared.RCL.Components.Dialogs;
using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

using Bunit;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using MudBlazor;
using MudBlazor.Services;

using NSubstitute;

namespace App.Shared.RCL.Tests;

public sealed class ActivityDayDetailDialogTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();
    private readonly IActivityStatisticsReader _stats = Substitute.For<IActivityStatisticsReader>();
    private readonly IUserTimeZoneService _timeZoneService = Substitute.For<IUserTimeZoneService>();
    private readonly IUserDateFormatService _dateFormatService = Substitute.For<IUserDateFormatService>();

    public ActivityDayDetailDialogTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();
        _ctx.Services.AddSingleton(_stats);
        _ctx.Services.AddSingleton(_timeZoneService);
        _ctx.Services.AddSingleton(_dateFormatService);

        _timeZoneService.ConvertToLocal(Arg.Any<DateTimeOffset>())
            .Returns(x => ((DateTimeOffset)x[0]!).ToLocalTime());
        _timeZoneService.LocalToday.Returns(new DateOnly(2026, 8, 12));
        _dateFormatService.Format(Arg.Any<DateOnly>()).Returns("2026-08-12");

        _ctx.Render<MudPopoverProvider>();
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    [Fact]
    public async Task Renders_Title_With_Date_And_Activity_Count()
    {
        // Arrange
        var dto = new ActivityDayDetailDto(
            new DateOnly(2026, 8, 12),
            [
                new ActivityDayEventDto(new DateTimeOffset(2026, 8, 12, 7, 30, 0, TimeSpan.Zero), ActivityEventType.HabitPlus, "", "Morning Run", null),
                new ActivityDayEventDto(new DateTimeOffset(2026, 8, 12, 8, 0, 0, TimeSpan.Zero), ActivityEventType.TimerSession, "", "Focus", 1500),
                new ActivityDayEventDto(new DateTimeOffset(2026, 8, 12, 9, 0, 0, TimeSpan.Zero), ActivityEventType.TodoComplete, "", "Buy milk", null)
            ],
            25);
        _stats.GetActivityDayDetailAsync(Arg.Any<DateOnly>(), Arg.Any<string?>())
            .Returns(Task.FromResult(dto));

        var provider = _ctx.Render<MudDialogProvider>();
        var dialogService = _ctx.Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<ActivityDayDetailDialog>
        {
            { x => x.Date, new DateOnly(2026, 8, 12) }
        };

        // Act
        await provider.InvokeAsync(async () => await dialogService.ShowAsync<ActivityDayDetailDialog>(string.Empty, parameters));
        await provider.WaitForStateAsync(() => provider.Markup.Contains("3 activities"), TimeSpan.FromSeconds(5));

        // Assert
        provider.Markup.Should().Contain("2026-08-12");
        provider.Markup.Should().Contain("2026-08-12 · 3 activities · 25 min focus");
        provider.Markup.Should().Contain("3 activities");
        provider.Markup.Should().NotContain("activity-day-detail-summary");
        provider.Markup.Should().NotContain("activity-day-detail-count");
    }

    [Fact]
    public async Task Renders_Distinct_Icons_Per_Event_Type()
    {
        // Arrange
        var dto = new ActivityDayDetailDto(
            new DateOnly(2026, 8, 12),
            [
                new ActivityDayEventDto(new DateTimeOffset(2026, 8, 12, 7, 30, 0, TimeSpan.Zero), ActivityEventType.HabitPlus, "", "Morning Run", null),
                new ActivityDayEventDto(new DateTimeOffset(2026, 8, 12, 7, 40, 0, TimeSpan.Zero), ActivityEventType.HabitMinus, "", "Morning Run", null),
                new ActivityDayEventDto(new DateTimeOffset(2026, 8, 12, 8, 0, 0, TimeSpan.Zero), ActivityEventType.DailyComplete, "", "Drink water", null),
                new ActivityDayEventDto(new DateTimeOffset(2026, 8, 12, 9, 0, 0, TimeSpan.Zero), ActivityEventType.TodoComplete, "", "Buy milk", null),
                new ActivityDayEventDto(new DateTimeOffset(2026, 8, 12, 10, 0, 0, TimeSpan.Zero), ActivityEventType.TimerSession, "", "Focus", 1500)
            ],
            25);
        _stats.GetActivityDayDetailAsync(Arg.Any<DateOnly>(), Arg.Any<string?>())
            .Returns(Task.FromResult(dto));

        var provider = _ctx.Render<MudDialogProvider>();
        var dialogService = _ctx.Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<ActivityDayDetailDialog>
        {
            { x => x.Date, new DateOnly(2026, 8, 12) }
        };

        // Act
        await provider.InvokeAsync(async () => await dialogService.ShowAsync<ActivityDayDetailDialog>(string.Empty, parameters));
        provider.Render();

        // Assert
        provider.Markup.Should().Contain("stats-day-timeline");
        provider.FindAll(".stats-day-event").Count.Should().Be(5);
        provider.FindAll("svg").Count.Should().BeGreaterThanOrEqualTo(5);
        provider.Markup.Should().Contain("stats-day-event__icon--plus");
        provider.Markup.Should().Contain("stats-day-event__icon--minus");
        provider.Markup.Should().Contain("stats-day-event__icon--daily");
        provider.Markup.Should().Contain("stats-day-event__icon--todo");
        provider.Markup.Should().Contain("stats-day-event__icon--timer");
        provider.Markup.Should().Contain("Focus");
    }
}
