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
    private readonly IUserNotifier _notifier = Substitute.For<IUserNotifier>();
    private readonly IBoardDataService _boardData = Substitute.For<IBoardDataService>();

    public ActivityDayDetailDialogTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();
        _ctx.Services.AddSingleton(_stats);
        _ctx.Services.AddSingleton(_timeZoneService);
        _ctx.Services.AddSingleton(_dateFormatService);
        _ctx.Services.AddSingleton<IUserNotifier>(_notifier);
        _ctx.Services.AddSingleton(_boardData);

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
    public async Task Renders_Header_And_Date_Without_Subtitle()
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
        await provider.WaitForStateAsync(() => provider.Markup.Contains("Morning Run"), TimeSpan.FromSeconds(5));

        // Assert
        provider.Markup.Should().Contain("Day details");
        provider.Markup.Should().Contain("2026-08-12");
        provider.Markup.Should().NotContain("3 activities");
        provider.Markup.Should().NotContain("25 min focus");
        provider.Markup.Should().NotContain("activity-day-detail-summary");
        provider.Markup.Should().NotContain("activity-day-detail-count");
        provider.Markup.Should().NotContain("day-stepper__today");
    }

    [Fact]
    public async Task Offers_retro_check_in_for_a_scheduled_past_day()
    {
        // Arrange
        var itemId = Guid.NewGuid();
        var daily = new BoardItem(
            itemId,
            "Meditate",
            DailyStartDate: new DateOnly(2026, 8, 1),
            DailyRepeat: DailyRepeatType.Daily,
            DailyRepeatInterval: 1);
        _boardData.GetItemAsync(itemId, Arg.Any<CancellationToken>()).Returns(Task.FromResult<BoardItem?>(daily));
        _stats.GetActivityDayDetailAsync(Arg.Any<DateOnly>(), Arg.Any<string?>())
            .Returns(Task.FromResult(new ActivityDayDetailDto(new DateOnly(2026, 8, 11), [], 0)));

        var provider = _ctx.Render<MudDialogProvider>();
        var dialogService = _ctx.Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<ActivityDayDetailDialog>
        {
            { x => x.Date, new DateOnly(2026, 8, 11) },
            { x => x.BoardItemId, itemId }
        };

        await provider.InvokeAsync(async () => await dialogService.ShowAsync<ActivityDayDetailDialog>(string.Empty, parameters));
        await provider.WaitForStateAsync(() => provider.Markup.Contains("Mark done for this day"), TimeSpan.FromSeconds(5));

        // Act
        var button = provider.FindAll("button").First(b => b.TextContent.Contains("Mark done for this day"));
        await provider.InvokeAsync(() => button.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));

        // Assert
        await _boardData.Received(1).CompleteDailyForDateAsync(itemId, new DateOnly(2026, 8, 11), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Close_reports_mutation_so_parents_reload()
    {
        var itemId = Guid.NewGuid();
        var daily = new BoardItem(
            itemId,
            "Meditate",
            DailyStartDate: new DateOnly(2026, 8, 1),
            DailyRepeat: DailyRepeatType.Daily,
            DailyRepeatInterval: 1);
        _boardData.GetItemAsync(itemId, Arg.Any<CancellationToken>()).Returns(Task.FromResult<BoardItem?>(daily));
        _boardData.CompleteDailyForDateAsync(itemId, new DateOnly(2026, 8, 11), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BoardItem?>(daily));
        _stats.GetActivityDayDetailAsync(Arg.Any<DateOnly>(), Arg.Any<string?>())
            .Returns(Task.FromResult(new ActivityDayDetailDto(new DateOnly(2026, 8, 11), [], 0)));

        var provider = _ctx.Render<MudDialogProvider>();
        var dialogService = _ctx.Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<ActivityDayDetailDialog>
        {
            { x => x.Date, new DateOnly(2026, 8, 11) },
            { x => x.BoardItemId, itemId }
        };

        IDialogReference? dialogRef = null;
        await provider.InvokeAsync(async () =>
        {
            dialogRef = await dialogService.ShowAsync<ActivityDayDetailDialog>(string.Empty, parameters);
        });
        await provider.WaitForStateAsync(() => provider.Markup.Contains("Mark done for this day"), TimeSpan.FromSeconds(5));

        var button = provider.FindAll("button").First(b => b.TextContent.Contains("Mark done for this day"));
        await provider.InvokeAsync(() => button.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));
        var close = provider.Find("button.hab-modal__close");
        await provider.InvokeAsync(() => close.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));

        var reference = dialogRef ?? throw new InvalidOperationException("DialogRef was null");
        var result = await reference.Result;

        result.Should().NotBeNull();
        result.Canceled.Should().BeFalse();
        result.Data.Should().Be(true);
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

    [Fact]
    public async Task RetroactiveActions_RenderInPinnedFooter_WhenActionable()
    {
        var itemId = Guid.NewGuid();
        var daily = new BoardItem(
            itemId,
            "Meditate",
            DailyStartDate: new DateOnly(2026, 8, 1),
            DailyRepeat: DailyRepeatType.Daily,
            DailyRepeatInterval: 1);
        _boardData.GetItemAsync(itemId, Arg.Any<CancellationToken>()).Returns(Task.FromResult<BoardItem?>(daily));
        _stats.GetActivityDayDetailAsync(Arg.Any<DateOnly>(), Arg.Any<string?>())
            .Returns(Task.FromResult(new ActivityDayDetailDto(new DateOnly(2026, 8, 11), [], 0)));

        var provider = _ctx.Render<MudDialogProvider>();
        var dialogService = _ctx.Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<ActivityDayDetailDialog>
        {
            { x => x.Date, new DateOnly(2026, 8, 11) },
            { x => x.BoardItemId, itemId }
        };

        await provider.InvokeAsync(async () => await dialogService.ShowAsync<ActivityDayDetailDialog>(string.Empty, parameters));
        await provider.WaitForStateAsync(() => provider.Markup.Contains("Skip this day"), TimeSpan.FromSeconds(5));

        provider.Markup.Should().Contain("hab-modal__footer--end");
        provider.Markup.Should().Contain("Skip this day");
        provider.Markup.Should().Contain("Mark done for this day");
    }

    [Fact]
    public async Task HidesFooterActions_WhenNotActionable()
    {
        _stats.GetActivityDayDetailAsync(Arg.Any<DateOnly>(), Arg.Any<string?>())
            .Returns(Task.FromResult(new ActivityDayDetailDto(new DateOnly(2026, 8, 12), [], 0)));

        var provider = _ctx.Render<MudDialogProvider>();
        var dialogService = _ctx.Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<ActivityDayDetailDialog>
        {
            { x => x.Date, new DateOnly(2026, 8, 12) }
        };

        await provider.InvokeAsync(async () => await dialogService.ShowAsync<ActivityDayDetailDialog>(string.Empty, parameters));
        await provider.WaitForStateAsync(() => provider.Markup.Contains("Day details"), TimeSpan.FromSeconds(5));

        provider.Markup.Should().NotContain("hab-modal__footer--end");
    }

    [Fact]
    public async Task Shows_today_toggle_when_viewing_today()
    {
        var today = DailySchedule.LocalToday(_timeZoneService);
        var itemId = Guid.NewGuid();
        var daily = new BoardItem(
            itemId,
            "Meditate",
            DailyStartDate: today.AddDays(-10),
            DailyRepeat: DailyRepeatType.Daily,
            DailyRepeatInterval: 1);
        _boardData.GetItemAsync(itemId, Arg.Any<CancellationToken>()).Returns(Task.FromResult<BoardItem?>(daily));
        _stats.GetActivityDayDetailAsync(Arg.Any<DateOnly>(), Arg.Any<string?>())
            .Returns(Task.FromResult(new ActivityDayDetailDto(today, [], 0)));

        var provider = _ctx.Render<MudDialogProvider>();
        var dialogService = _ctx.Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<ActivityDayDetailDialog>
        {
            { x => x.Date, today },
            { x => x.BoardItemId, itemId }
        };

        await provider.InvokeAsync(async () => await dialogService.ShowAsync<ActivityDayDetailDialog>(string.Empty, parameters));
        await provider.WaitForStateAsync(() => provider.Markup.Contains("Mark done for today"), TimeSpan.FromSeconds(5));

        var button = provider.FindAll("button").First(b => b.TextContent.Contains("Mark done for today"));
        await provider.InvokeAsync(() => button.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));

        await _boardData.Received(1).ToggleItemAsync(BoardSection.Daily, itemId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Shows_skip_when_today_already_done_viewing_past()
    {
        var today = DailySchedule.LocalToday(_timeZoneService);
        var past = today.AddDays(-1);
        var itemId = Guid.NewGuid();
        var daily = new BoardItem(
            itemId,
            "Meditate",
            DailyStartDate: today.AddDays(-10),
            DailyRepeat: DailyRepeatType.Daily,
            DailyRepeatInterval: 1,
            DailyLastCompletedOn: today);
        _boardData.GetItemAsync(itemId, Arg.Any<CancellationToken>()).Returns(Task.FromResult<BoardItem?>(daily));
        _stats.GetActivityDayDetailAsync(Arg.Any<DateOnly>(), Arg.Any<string?>())
            .Returns(Task.FromResult(new ActivityDayDetailDto(past, [], 0)));

        var provider = _ctx.Render<MudDialogProvider>();
        var dialogService = _ctx.Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<ActivityDayDetailDialog>
        {
            { x => x.Date, past },
            { x => x.BoardItemId, itemId }
        };

        await provider.InvokeAsync(async () => await dialogService.ShowAsync<ActivityDayDetailDialog>(string.Empty, parameters));
        await provider.WaitForStateAsync(() => provider.Markup.Contains("Mark done for this day"), TimeSpan.FromSeconds(5));

        provider.Markup.Should().Contain("hab-modal__footer--end");
        var skip = provider.FindAll("button").First(b => b.TextContent.Contains("Skip this day"));
        skip.HasAttribute("disabled").Should().BeFalse();
        var mark = provider.FindAll("button").First(b => b.TextContent.Contains("Mark done for this day"));
        mark.HasAttribute("disabled").Should().BeFalse();
        await provider.InvokeAsync(() => mark.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));

        await _boardData.Received(1).CompleteDailyForDateAsync(itemId, past, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Omits_day_section_when_no_item_selected()
    {
        var today = DailySchedule.LocalToday(_timeZoneService);
        var past = today.AddDays(-2);
        var itemId = Guid.NewGuid();
        var daily = new BoardItem(
            itemId,
            "Workout",
            DailyStartDate: today.AddDays(-10),
            DailyRepeat: DailyRepeatType.Daily,
            DailyRepeatInterval: 1);
        _boardData.GetSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BoardSnapshot([], [daily], [])));
        _stats.GetActivityDayDetailAsync(Arg.Any<DateOnly>(), Arg.Any<string?>())
            .Returns(Task.FromResult(new ActivityDayDetailDto(past, [], 0)));

        var provider = _ctx.Render<MudDialogProvider>();
        var dialogService = _ctx.Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<ActivityDayDetailDialog>
        {
            { x => x.Date, past }
        };

        await provider.InvokeAsync(async () => await dialogService.ShowAsync<ActivityDayDetailDialog>(string.Empty, parameters));
        await provider.WaitForStateAsync(() => provider.Markup.Contains("No activity this day"), TimeSpan.FromSeconds(5));

        provider.Markup.Should().NotContain("Dailies this day");
        provider.Markup.Should().NotContain("Workout");
        await _boardData.DidNotReceive().CompleteDailyForDateAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Shows_no_day_section_actions_without_item_selected()
    {
        var today = DailySchedule.LocalToday(_timeZoneService);
        var past = today.AddDays(-3);
        var itemId = Guid.NewGuid();
        var daily = new BoardItem(
            itemId,
            "Workout",
            DailyStartDate: today.AddDays(-10),
            DailyRepeat: DailyRepeatType.Daily,
            DailyRepeatInterval: 1);
        _boardData.GetSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BoardSnapshot([], [daily], [])));
        _stats.GetActivityDayDetailAsync(Arg.Any<DateOnly>(), Arg.Any<string?>())
            .Returns(Task.FromResult(new ActivityDayDetailDto(past, [], 0)));

        var provider = _ctx.Render<MudDialogProvider>();
        var dialogService = _ctx.Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<ActivityDayDetailDialog>
        {
            { x => x.Date, past }
        };

        await provider.InvokeAsync(async () => await dialogService.ShowAsync<ActivityDayDetailDialog>(string.Empty, parameters));
        await provider.WaitForStateAsync(() => provider.Markup.Contains("No activity this day"), TimeSpan.FromSeconds(5));

        provider.Markup.Should().NotContain("Dailies this day");
        await _boardData.DidNotReceive().SkipDailyForDateAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Marks_done_on_day_outside_current_schedule()
    {
        var today = DailySchedule.LocalToday(_timeZoneService);
        var past = today.AddDays(-1);
        if (past.Day == 15)
        {
            past = past.AddDays(-1);
        }

        var itemId = Guid.NewGuid();
        var daily = new BoardItem(
            itemId,
            "Workout",
            DailyStartDate: new DateOnly(past.Year, past.Month, 15).AddMonths(-1),
            DailyRepeat: DailyRepeatType.Monthly,
            DailyRepeatInterval: 1);
        _boardData.GetItemAsync(itemId, Arg.Any<CancellationToken>()).Returns(Task.FromResult<BoardItem?>(daily));
        _stats.GetActivityDayDetailAsync(Arg.Any<DateOnly>(), Arg.Any<string?>())
            .Returns(Task.FromResult(new ActivityDayDetailDto(past,
            [
                new ActivityDayEventDto(new DateTimeOffset(past.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), ActivityEventType.DailyComplete, "", "Workout", null)
            ], 0)));

        var provider = _ctx.Render<MudDialogProvider>();
        var dialogService = _ctx.Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<ActivityDayDetailDialog>
        {
            { x => x.Date, past },
            { x => x.BoardItemId, itemId }
        };

        await provider.InvokeAsync(async () => await dialogService.ShowAsync<ActivityDayDetailDialog>(string.Empty, parameters));
        await provider.WaitForStateAsync(() => provider.Markup.Contains("Skip this day"), TimeSpan.FromSeconds(5));

        provider.Markup.Should().Contain("hab-modal__footer--end");
        var skip = provider.FindAll("button").First(b => b.TextContent.Contains("Skip this day"));
        skip.HasAttribute("disabled").Should().BeFalse();
        var mark = provider.FindAll("button").First(b => b.TextContent.Contains("Mark done for this day"));
        mark.HasAttribute("disabled").Should().BeFalse();
        await provider.InvokeAsync(() => mark.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));

        await _boardData.Received(1).CompleteDailyForDateAsync(itemId, past, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Omits_day_section_when_item_lookup_fails()
    {
        var today = DailySchedule.LocalToday(_timeZoneService);
        var past = today.AddDays(-4);
        var missingId = Guid.NewGuid();
        var daily = new BoardItem(
            Guid.NewGuid(),
            "Workout",
            DailyStartDate: today.AddDays(-10),
            DailyRepeat: DailyRepeatType.Daily,
            DailyRepeatInterval: 1);
        _boardData.GetItemAsync(missingId, Arg.Any<CancellationToken>()).Returns(Task.FromResult<BoardItem?>(null));
        _boardData.GetSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BoardSnapshot([], [daily], [])));
        _stats.GetActivityDayDetailAsync(Arg.Any<DateOnly>(), Arg.Any<string?>())
            .Returns(Task.FromResult(new ActivityDayDetailDto(past, [], 0)));

        var provider = _ctx.Render<MudDialogProvider>();
        var dialogService = _ctx.Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<ActivityDayDetailDialog>
        {
            { x => x.Date, past },
            { x => x.BoardItemId, missingId }
        };

        await provider.InvokeAsync(async () => await dialogService.ShowAsync<ActivityDayDetailDialog>(string.Empty, parameters));
        await provider.WaitForStateAsync(() => provider.Markup.Contains("No activity this day"), TimeSpan.FromSeconds(5));

        provider.Markup.Should().NotContain("Dailies this day");
        provider.Markup.Should().NotContain("Workout");
    }

    [Fact]
    public async Task Footer_shows_due_note_with_working_actions_when_not_due()
    {
        var today = DailySchedule.LocalToday(_timeZoneService);
        var past = today.AddDays(-1);
        if (past.Day == 15)
        {
            past = past.AddDays(-1);
        }

        var itemId = Guid.NewGuid();
        var daily = new BoardItem(
            itemId,
            "Workout",
            DailyStartDate: new DateOnly(past.Year, past.Month, 15).AddMonths(-1),
            DailyRepeat: DailyRepeatType.Monthly,
            DailyRepeatInterval: 1);
        _boardData.GetItemAsync(itemId, Arg.Any<CancellationToken>()).Returns(Task.FromResult<BoardItem?>(daily));
        _stats.GetActivityDayDetailAsync(Arg.Any<DateOnly>(), Arg.Any<string?>())
            .Returns(Task.FromResult(new ActivityDayDetailDto(past, [], 0)));

        var provider = _ctx.Render<MudDialogProvider>();
        var dialogService = _ctx.Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<ActivityDayDetailDialog>
        {
            { x => x.Date, past },
            { x => x.BoardItemId, itemId }
        };

        await provider.InvokeAsync(async () => await dialogService.ShowAsync<ActivityDayDetailDialog>(string.Empty, parameters));
        await provider.WaitForStateAsync(() => provider.Markup.Contains("Not due this day"), TimeSpan.FromSeconds(5));

        provider.Markup.Should().Contain("hab-modal__footer--end");
        provider.Markup.Should().Contain("Monthly");
        var skip = provider.FindAll("button").First(b => b.TextContent.Contains("Skip this day"));
        skip.HasAttribute("disabled").Should().BeFalse();
        var mark = provider.FindAll("button").First(b => b.TextContent.Contains("Mark done for this day"));
        mark.HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public async Task Footer_shows_due_note_when_due()
    {
        var today = DailySchedule.LocalToday(_timeZoneService);
        var past = today.AddDays(-2);
        var itemId = Guid.NewGuid();
        var daily = new BoardItem(
            itemId,
            "Meditate",
            DailyStartDate: today.AddDays(-10),
            DailyRepeat: DailyRepeatType.Daily,
            DailyRepeatInterval: 1);
        _boardData.GetItemAsync(itemId, Arg.Any<CancellationToken>()).Returns(Task.FromResult<BoardItem?>(daily));
        _stats.GetActivityDayDetailAsync(Arg.Any<DateOnly>(), Arg.Any<string?>())
            .Returns(Task.FromResult(new ActivityDayDetailDto(past, [], 0)));

        var provider = _ctx.Render<MudDialogProvider>();
        var dialogService = _ctx.Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<ActivityDayDetailDialog>
        {
            { x => x.Date, past },
            { x => x.BoardItemId, itemId }
        };

        await provider.InvokeAsync(async () => await dialogService.ShowAsync<ActivityDayDetailDialog>(string.Empty, parameters));
        await provider.WaitForStateAsync(() => provider.Markup.Contains("Due this day"), TimeSpan.FromSeconds(5));

        provider.Markup.Should().Contain("Daily");
    }
}
