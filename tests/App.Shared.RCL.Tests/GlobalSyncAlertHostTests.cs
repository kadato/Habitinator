using System;
using System.Threading;
using System.Threading.Tasks;

using App.Shared.RCL.Components;
using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

using Bunit;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using MudBlazor;

using NSubstitute;

using Xunit;

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
        // Arrange
        _settingsService.Settings = new NotificationSettings { SyncFailureAlertsEnabled = true };
        _boardSync.IsOffline = false;
        var cut = _ctx.Render<GlobalSyncAlertHost>();

        // Act - Transition to Offline
        _boardSync.IsOffline = true;
        await cut.InvokeAsync(() => _boardSync.RaiseChanged());

        // Assert
        await _notifier.Received(1).NotifyAsync(
            "Working offline. Changes will save locally and sync when you reconnect.",
            Severity.Warning);
    }

    [Fact]
    public async Task Alerts_On_Online_Transition_When_Enabled()
    {
        // Arrange
        _settingsService.Settings = new NotificationSettings { SyncFailureAlertsEnabled = true };
        _boardSync.IsOffline = true;
        var cut = _ctx.Render<GlobalSyncAlertHost>();

        // Act - Transition to Online
        _boardSync.IsOffline = false;
        await cut.InvokeAsync(() => _boardSync.RaiseChanged());

        // Assert
        await _notifier.Received(1).NotifyAsync(
            "Back online. Connection to the server restored.",
            Severity.Success);
    }

    [Fact]
    public async Task Alerts_On_Sync_Problem_Transition_When_Enabled()
    {
        // Arrange
        _settingsService.Settings = new NotificationSettings { SyncFailureAlertsEnabled = true };
        _boardSync.SyncProblemMessage = null;
        var cut = _ctx.Render<GlobalSyncAlertHost>();

        // Act - Transition to Sync Problem
        _boardSync.SyncProblemMessage = "Stuck outbox item.";
        await cut.InvokeAsync(() => _boardSync.RaiseChanged());

        // Assert
        await _notifier.Received(1).NotifyAsync(
            "Stuck outbox item.",
            Severity.Error);
    }

    [Fact]
    public async Task Alerts_On_Sync_Problem_Resolved_Transition_When_Enabled()
    {
        // Arrange
        _settingsService.Settings = new NotificationSettings { SyncFailureAlertsEnabled = true };
        _boardSync.SyncProblemMessage = "Stuck outbox item.";
        var cut = _ctx.Render<GlobalSyncAlertHost>();

        // Act - Transition to Null (resolved)
        _boardSync.SyncProblemMessage = null;
        await cut.InvokeAsync(() => _boardSync.RaiseChanged());

        // Assert
        await _notifier.Received(1).NotifyAsync(
            "Sync issues resolved. All changes synchronized.",
            Severity.Success);
    }

    [Fact]
    public async Task Suppress_Problem_Toast_If_Offline_Reporting()
    {
        // Arrange
        _settingsService.Settings = new NotificationSettings { SyncFailureAlertsEnabled = true };
        _boardSync.IsOffline = false;
        _boardSync.SyncProblemMessage = null;
        var cut = _ctx.Render<GlobalSyncAlertHost>();

        // Act - Transition to Offline and Offline problem message simultaneously
        _boardSync.IsOffline = true;
        _boardSync.SyncProblemMessage = "Offline — board changes stay on this device...";
        await cut.InvokeAsync(() => _boardSync.RaiseChanged());

        // Assert - Warn was sent, Error was suppressed
        await _notifier.Received(1).NotifyAsync(
            "Working offline. Changes will save locally and sync when you reconnect.",
            Severity.Warning);
        await _notifier.DidNotReceive().NotifyAsync(
            Arg.Is<string>(s => s.Contains("Offline")),
            Severity.Error);
    }

    [Fact]
    public async Task No_Alerts_When_Disabled()
    {
        // Arrange
        _settingsService.Settings = new NotificationSettings { SyncFailureAlertsEnabled = false };
        _boardSync.IsOffline = false;
        var cut = _ctx.Render<GlobalSyncAlertHost>();

        // Act - Transition to Offline
        _boardSync.IsOffline = true;
        await cut.InvokeAsync(() => _boardSync.RaiseChanged());

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

        public event Action? Changed;
    }
}
