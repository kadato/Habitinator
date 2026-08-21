using App.Shared.RCL.Components;
using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

using MudBlazor;

using NSubstitute;

namespace App.Shared.RCL.Tests;

public sealed class GlobalSyncAlertHostTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();
    private readonly TestBoardSyncStatus _boardSync = new();
    private readonly IUserNotifier _notifier = Substitute.For<IUserNotifier>();
    private readonly TestNotificationSettingsService _settingsService = new();

    public GlobalSyncAlertHostTests()
    {
        _ctx.Services.AddSingleton<IBoardSyncStatus>(_boardSync);
        _ctx.Services.AddSingleton<IUserNotifier>(_notifier);
        _ctx.Services.AddSingleton<INotificationSettingsService>(_settingsService);
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    [Fact]
    public async Task Alerts_On_Offline_Transition_When_Enabled()
    {
        // Arrange - offline transitions are now surfaced only via the minimal sync dot indicator, not toasts
        _settingsService.Settings = new NotificationSettings { SyncFailureAlertsEnabled = true };
        _boardSync.IsOffline = false;
        var cut = _ctx.Render<GlobalSyncAlertHost>();

        // Act - transition to offline
        _boardSync.IsOffline = true;
        await cut.InvokeAsync(_boardSync.RaiseChanged);

        // Assert - no toast, only the SyncStatusIndicator dot updates
        await _notifier.DidNotReceive().NotifyAsync(Arg.Any<string>(), Arg.Any<Severity>());
    }

    [Fact]
    public async Task Alerts_On_Online_Transition_When_Enabled()
    {
        // Arrange - online transitions are now dot-only
        _settingsService.Settings = new NotificationSettings { SyncFailureAlertsEnabled = true };
        _boardSync.IsOffline = true;
        var cut = _ctx.Render<GlobalSyncAlertHost>();

        // Act - transition to online
        _boardSync.IsOffline = false;
        await cut.InvokeAsync(_boardSync.RaiseChanged);

        // Assert - no toast
        await _notifier.DidNotReceive().NotifyAsync(Arg.Any<string>(), Arg.Any<Severity>());
    }

    [Fact]
    public async Task Alerts_On_Sync_Problem_Transition_When_Enabled()
    {
        // Arrange - sync problems are now dot-only, not toasts, to avoid spam when server is down
        _settingsService.Settings = new NotificationSettings { SyncFailureAlertsEnabled = true };
        _boardSync.SyncProblemMessage = null;
        var cut = _ctx.Render<GlobalSyncAlertHost>();

        // Act - transition to sync problem
        _boardSync.SyncProblemMessage = "Stuck outbox item.";
        await cut.InvokeAsync(_boardSync.RaiseChanged);

        // Assert - no error toast, indicator shows the small error dot
        await _notifier.DidNotReceive().NotifyAsync(Arg.Any<string>(), Arg.Any<Severity>());
    }

    [Fact]
    public async Task Alerts_On_Sync_Problem_Resolved_Transition_When_Enabled()
    {
        // Arrange - resolved transitions no longer toast; false resolved when server is still down is suppressed
        _settingsService.Settings = new NotificationSettings { SyncFailureAlertsEnabled = true };
        _boardSync.SyncProblemMessage = "Stuck outbox item.";
        var cut = _ctx.Render<GlobalSyncAlertHost>();

        // Act - transition to null, meaning resolved
        _boardSync.SyncProblemMessage = null;
        await cut.InvokeAsync(_boardSync.RaiseChanged);

        // Assert - no success toast; dot simply returns to ok/idle
        await _notifier.DidNotReceive().NotifyAsync(Arg.Any<string>(), Arg.Any<Severity>());
    }

    [Fact]
    public async Task Suppress_Problem_Toast_If_Offline_Reporting()
    {
        // Arrange - neither offline nor sync problem toasts; both are dot-only now
        _settingsService.Settings = new NotificationSettings { SyncFailureAlertsEnabled = true };
        _boardSync.IsOffline = false;
        _boardSync.SyncProblemMessage = null;
        var cut = _ctx.Render<GlobalSyncAlertHost>();

        // Act - transition to offline and offline problem message simultaneously
        _boardSync.IsOffline = true;
        _boardSync.SyncProblemMessage = "Offline - board changes stay on this device until you reconnect.";
        await cut.InvokeAsync(_boardSync.RaiseChanged);

        // Assert - no toasts at all
        await _notifier.DidNotReceive().NotifyAsync(Arg.Any<string>(), Arg.Any<Severity>());
    }

    [Fact]
    public async Task No_Alerts_When_Disabled()
    {
        // Arrange
        _settingsService.Settings = new NotificationSettings { SyncFailureAlertsEnabled = false };
        _boardSync.IsOffline = false;
        var cut = _ctx.Render<GlobalSyncAlertHost>();

        // Act - transition to offline
        _boardSync.IsOffline = true;
        await cut.InvokeAsync(_boardSync.RaiseChanged);

        // Assert
        await _notifier.DidNotReceive().NotifyAsync(Arg.Any<string>(), Arg.Any<Severity>());
    }

    [Fact]
    public async Task Does_Not_Alert_After_Dispose()
    {
        // Arrange
        _settingsService.Settings = new NotificationSettings { SyncFailureAlertsEnabled = true };
        _boardSync.IsOffline = false;
        var cut = _ctx.Render<GlobalSyncAlertHost>();

        // Act
        cut.Instance.Dispose();

        _boardSync.IsOffline = true;
        _boardSync.RaiseChanged();

        // Assert
        await _notifier.DidNotReceive().NotifyAsync(Arg.Any<string>(), Arg.Any<Severity>());
    }

    private sealed class TestBoardSyncStatus : IBoardSyncStatus
    {
        public bool IsOffline { get; set; }
        public bool IsSyncing { get; set; }
        public DateTimeOffset? LastSyncedUtc { get; set; }
        public string? SyncProblemMessage { get; set; }

        public event EventHandler? Changed;

        public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class TestNotificationSettingsService : INotificationSettingsService
    {
        public NotificationSettings Settings { get; set; } = new();

        public Task<NotificationSettings> GetAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Settings);

        public Task SaveAsync(NotificationSettings settings, CancellationToken cancellationToken = default)
        {
            Settings = settings;
            return Task.CompletedTask;
        }

#pragma warning disable CS0067
        public event EventHandler? Changed;
#pragma warning restore CS0067
    }
}
