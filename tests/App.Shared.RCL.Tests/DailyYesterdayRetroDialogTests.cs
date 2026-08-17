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

public sealed class DailyYesterdayRetroDialogTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();
    private readonly IUserDateFormatService _dateFormatService = Substitute.For<IUserDateFormatService>();
    private readonly IBoardDataService _boardDataService = Substitute.For<IBoardDataService>();
    private readonly IUserNotifier _notifier = Substitute.For<IUserNotifier>();

    public DailyYesterdayRetroDialogTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();
        _ctx.Services.AddSingleton(_dateFormatService);
        _ctx.Services.AddSingleton(_boardDataService);
        _ctx.Services.AddSingleton(_notifier);

        _dateFormatService.Format(Arg.Any<DateOnly>()).Returns("2026-08-11");
        _ctx.Render<MudPopoverProvider>();
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    [Fact]
    public async Task Renders_Yesterday_Dailies_And_Done_Footer_Button()
    {
        // Arrange
        var daily1 = new BoardItem(Guid.NewGuid(), "Drink 2L Water", false, 0);
        var daily2 = new BoardItem(Guid.NewGuid(), "Read 20 pages", false, 0);
        var yesterday = new DateOnly(2026, 8, 11);

        var provider = _ctx.Render<MudDialogProvider>();
        var dialogService = _ctx.Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<DailyYesterdayRetroDialog>
        {
            { x => x.DueOn, yesterday },
            { x => x.Items, new List<BoardItem> { daily1, daily2 } }
        };

        // Act
        await provider.InvokeAsync(async () => await dialogService.ShowAsync<DailyYesterdayRetroDialog>(string.Empty, parameters));

        // Assert
        provider.Markup.Should().Contain("Yesterday's Dailies");
        provider.Markup.Should().Contain("Drink 2L Water");
        provider.Markup.Should().Contain("Read 20 pages");
        provider.Markup.Should().Contain("Done");
    }

    [Fact]
    public async Task Checking_Item_And_Clicking_Done_Completes_Daily_For_Yesterday()
    {
        // Arrange
        var daily1Id = Guid.NewGuid();
        var daily1 = new BoardItem(daily1Id, "Morning Meditation", false, 0);
        var yesterday = new DateOnly(2026, 8, 11);

        _boardDataService.CompleteDailyForDateAsync(daily1Id, yesterday)
            .Returns(Task.FromResult<BoardItem?>(daily1 with { Counter = 1, DailyLastCompletedOn = yesterday }));

        var provider = _ctx.Render<MudDialogProvider>();
        var dialogService = _ctx.Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<DailyYesterdayRetroDialog>
        {
            { x => x.DueOn, yesterday },
            { x => x.Items, new List<BoardItem> { daily1 } }
        };

        // Act
        IDialogReference? dialogRef = null;
        await provider.InvokeAsync(async () =>
        {
            dialogRef = await dialogService.ShowAsync<DailyYesterdayRetroDialog>(string.Empty, parameters);
        });

        var row = provider.Find(".daily-yesterday-row");
        await provider.InvokeAsync(() => row.Click());

        var doneBtn = provider.Find(".daily-yesterday-footer__close-btn");
        await provider.InvokeAsync(() => doneBtn.Click());

        var reference = dialogRef ?? throw new InvalidOperationException("DialogRef was null");
        var result = await reference.Result;

        // Assert
        result.Should().NotBeNull();
        result.Canceled.Should().BeFalse();
        await _boardDataService.Received(1).CompleteDailyForDateAsync(daily1Id, yesterday);
    }

    [Fact]
    public async Task Checking_Item_And_Dismissing_Via_Close_Button_Completes_Daily_For_Yesterday()
    {
        // Arrange
        var daily1Id = Guid.NewGuid();
        var daily1 = new BoardItem(daily1Id, "Evening Walk", false, 0);
        var yesterday = new DateOnly(2026, 8, 11);

        _boardDataService.CompleteDailyForDateAsync(daily1Id, yesterday)
            .Returns(Task.FromResult<BoardItem?>(daily1 with { Counter = 1, DailyLastCompletedOn = yesterday }));

        var provider = _ctx.Render<MudDialogProvider>();
        var dialogService = _ctx.Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<DailyYesterdayRetroDialog>
        {
            { x => x.DueOn, yesterday },
            { x => x.Items, new List<BoardItem> { daily1 } }
        };

        // Act
        IDialogReference? dialogRef = null;
        await provider.InvokeAsync(async () =>
        {
            dialogRef = await dialogService.ShowAsync<DailyYesterdayRetroDialog>(string.Empty, parameters);
        });

        var row = provider.Find(".daily-yesterday-row");
        await provider.InvokeAsync(() => row.Click());

        var closeBtn = provider.Find(".daily-yesterday-header__close-btn");
        await provider.InvokeAsync(() => closeBtn.Click());

        var reference = dialogRef ?? throw new InvalidOperationException("DialogRef was null");
        var result = await reference.Result;

        // Assert
        result.Should().NotBeNull();
        result.Canceled.Should().BeFalse();
        await _boardDataService.Received(1).CompleteDailyForDateAsync(daily1Id, yesterday);
    }
}
