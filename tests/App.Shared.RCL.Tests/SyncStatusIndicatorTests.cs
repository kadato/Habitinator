#pragma warning disable MUD0012

using App.Shared.RCL.Components;
using App.Shared.RCL.Services;
using App.Shared.RCL.Services.Board.Local;

using Bunit;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using MudBlazor.Services;

using NSubstitute;

namespace App.Shared.RCL.Tests;

public sealed class SyncStatusIndicatorTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();
    private readonly TestBoardSyncStatus _boardSync = new();
    private readonly IBoardSyncRequestor _syncRequestor = Substitute.For<IBoardSyncRequestor>();
    private readonly IUserDateFormatService _dateFormat = Substitute.For<IUserDateFormatService>();
    private readonly IUserNotifier _notifier = Substitute.For<IUserNotifier>();

    public SyncStatusIndicatorTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();
        _ctx.Services.AddSingleton<IBoardSyncStatus>(_boardSync);
        _ctx.Services.AddSingleton(_syncRequestor);
        _ctx.Services.AddSingleton(_dateFormat);
        _ctx.Services.AddSingleton(_notifier);
        _dateFormat.Format(Arg.Any<DateTime>()).Returns("2026-10-02");
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    [Fact]
    public void FullRow_ShowsPendingCount_And_Retry_Calls_RequestSync()
    {
        _boardSync.SyncProblemMessage = "boom";
        _boardSync.PendingCount = 2;

        var cut = _ctx.Render<SyncStatusIndicator>(p => p.Add(x => x.IconOnly, false));

        cut.Markup.Should().Contain("2 pending");
        var retry = cut.Find(".sync-row__retry");
        retry.TextContent.Should().Contain("Retry now");

        retry.Click();

        _syncRequestor.Received(1).RequestSync(Arg.Any<bool>());
    }

    [Fact]
    public void FullRow_HidesRetry_When_Idle_With_No_Pending()
    {
        var cut = _ctx.Render<SyncStatusIndicator>(p => p.Add(x => x.IconOnly, false));

        cut.FindAll(".sync-row__retry").Should().BeEmpty();
        cut.Markup.Should().Contain("Not synced");
    }

    [Fact]
    public void IconOnly_AnnouncesPendingCount_In_Aria_Label()
    {
        _boardSync.PendingCount = 3;

        var cut = _ctx.Render<SyncStatusIndicator>(p => p.Add(x => x.IconOnly, true));

        var button = cut.Find(".sync-icon-btn");
        button.GetAttribute("aria-label").Should().Contain("3 pending");
    }

    private sealed class TestBoardSyncStatus : IBoardSyncStatus
    {
        public bool IsOffline { get; set; }
        public bool IsSyncing { get; set; }
        public DateTimeOffset? LastSyncedUtc { get; set; }
        public int PendingCount { get; set; }
        public string? SyncProblemMessage { get; set; }

#pragma warning disable CS0067
        public event EventHandler? Changed;
#pragma warning restore CS0067
    }
}
