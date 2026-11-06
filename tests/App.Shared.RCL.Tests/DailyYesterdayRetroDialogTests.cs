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
    private readonly App.Shared.RCL.Services.Board.Local.IBoardSyncRequestor _syncRequestor = Substitute.For<App.Shared.RCL.Services.Board.Local.IBoardSyncRequestor>();
    private readonly TestBoardSyncStatus _boardSync = new();

    public DailyYesterdayRetroDialogTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();
        _ctx.Services.AddSingleton(_dateFormatService);
        _ctx.Services.AddSingleton(_boardDataService);
        _ctx.Services.AddSingleton(_notifier);
        _ctx.Services.AddSingleton(_syncRequestor);
        _ctx.Services.AddSingleton<IBoardSyncStatus>(_boardSync);

        _dateFormatService.Format(Arg.Any<DateOnly>()).Returns("2026-08-11");
        _ctx.Render<MudPopoverProvider>();
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    [Fact]
    public async Task Renders_Yesterday_Dailies_And_Save_Footer_Button()
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
        provider.Markup.Should().Contain("Yesterday's dailies");
        provider.Markup.Should().Contain("Drink 2L Water");
        provider.Markup.Should().Contain("Read 20 pages");
        provider.Markup.Should().Contain("Save");
    }

    [Fact]
    public async Task Checking_Item_And_Clicking_Save_Completes_Daily_For_Yesterday()
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

        var saveBtn = provider.Find(".hab-modal__footer button");
        await provider.InvokeAsync(() => saveBtn.Click());

        var reference = dialogRef ?? throw new InvalidOperationException("DialogRef was null");
        var result = await reference.Result;

        // Assert
        result.Should().NotBeNull();
        result.Canceled.Should().BeFalse();
        await _boardDataService.Received(1).CompleteDailyForDateAsync(daily1Id, yesterday);
    }

    [Fact]
    public async Task Checking_Item_And_Clicking_Backdrop_Completes_Daily_For_Yesterday()
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

        var dialog = provider.FindComponent<MudDialog>();
        await provider.InvokeAsync(() => dialog.Instance.OnBackdropClick.InvokeAsync(null));

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

        var closeBtn = provider.Find(".hab-modal__close");
        await provider.InvokeAsync(() => closeBtn.Click());

        var reference = dialogRef ?? throw new InvalidOperationException("DialogRef was null");
        var result = await reference.Result;

        // Assert
        result.Should().NotBeNull();
        result.Canceled.Should().BeFalse();
        await _boardDataService.Received(1).CompleteDailyForDateAsync(daily1Id, yesterday);
    }

    [Fact]
    public async Task Checking_Item_And_Saving_Drains_Board_Sync_When_Requestor_Available()
    {
        // Arrange
        var daily1Id = Guid.NewGuid();
        var daily1 = new BoardItem(daily1Id, "Stretch", false, 0);
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

        var saveBtn = provider.Find(".hab-modal__footer button");
        await provider.InvokeAsync(() => saveBtn.Click());

        var reference = dialogRef ?? throw new InvalidOperationException("DialogRef was null");
        var result = await reference.Result;

        // Assert
        result.Should().NotBeNull();
        result.Canceled.Should().BeFalse();
        await _syncRequestor.Received(1).SyncNowAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task No_Items_Checked_And_Saving_Does_Not_Drain_Board_Sync()
    {
        // Arrange
        _syncRequestor.ClearReceivedCalls();
        var daily1Id = Guid.NewGuid();
        var daily1 = new BoardItem(daily1Id, "Stretch", false, 0);
        var yesterday = new DateOnly(2026, 8, 11);

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

        var saveBtn = provider.Find(".hab-modal__footer button");
        await provider.InvokeAsync(() => saveBtn.Click());

        var reference = dialogRef ?? throw new InvalidOperationException("DialogRef was null");
        var result = await reference.Result;

        // Assert
        result.Should().NotBeNull();
        result.Canceled.Should().BeTrue();
        await _syncRequestor.DidNotReceive().SyncNowAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Checking_Item_And_Saving_Does_Not_Drain_When_Sync_Paused()
    {
        // Arrange
        _boardSync.IsPaused = true;
        try
        {
            var daily1Id = Guid.NewGuid();
            var daily1 = new BoardItem(daily1Id, "Stretch", false, 0);
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

            var saveBtn = provider.Find(".hab-modal__footer button");
            await provider.InvokeAsync(() => saveBtn.Click());

            var reference = dialogRef ?? throw new InvalidOperationException("DialogRef was null");
            var result = await reference.Result;

            // Assert
            result.Should().NotBeNull();
            result.Canceled.Should().BeFalse();
            await _syncRequestor.DidNotReceive().SyncNowAsync(Arg.Any<CancellationToken>());
            await _boardDataService.Received(1).CompleteDailyForDateAsync(daily1Id, yesterday);
        }
        finally
        {
            _boardSync.IsPaused = false;
        }
    }

    private sealed class TestBoardSyncStatus : IBoardSyncStatus
    {
        public bool IsOffline { get; set; }
        public bool IsSyncing { get; set; }
        public bool IsPaused { get; set; }
        public DateTimeOffset? LastSyncedUtc { get; set; }
        public int PendingCount { get; set; }
        public string? SyncProblemMessage { get; set; }
        public IReadOnlyList<App.Shared.RCL.Services.Board.Local.BoardSyncConflict> RecentConflicts { get; set; } = [];

#pragma warning disable CS0067
        public event EventHandler? Changed;
#pragma warning restore CS0067
    }
}
