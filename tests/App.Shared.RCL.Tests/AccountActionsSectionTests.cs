#pragma warning disable MUD0012

using App.Shared.RCL.Components;
using App.Shared.RCL.Services;

using Bunit;

using FluentAssertions;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

using MudBlazor;
using MudBlazor.Services;

using NSubstitute;

namespace App.Shared.RCL.Tests;

public sealed class AccountActionsSectionTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();
    private readonly IRenderedComponent<MudDialogProvider> _dialogProvider;
    private readonly IAccountActionsService _accountActions = Substitute.For<IAccountActionsService>();
    private readonly IUserNotifier _notifier = Substitute.For<IUserNotifier>();
    private readonly IUserDataExportService _export = Substitute.For<IUserDataExportService>();

    public AccountActionsSectionTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();
        _ctx.Services.AddSingleton<IAccountActionsService>(_accountActions);
        _ctx.Services.AddSingleton<IUserNotifier>(_notifier);
        _ctx.Services.AddSingleton<IUserDataExportService>(_export);
        _export.ExportAsync(Arg.Any<CancellationToken>())
            .Returns(new UserDataExportDto(DateTimeOffset.UtcNow, [], []));

        // Render PopoverProvider and DialogProvider to satisfy MudBlazor components
        _ctx.Render<MudPopoverProvider>();
        _dialogProvider = _ctx.Render<MudDialogProvider>();
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    [Fact]
    public void Renders_AccountActions_Correctly()
    {
        // Act
        var cut = _ctx.Render<AccountActionsSection>();

        // Assert
        var inputs = cut.FindComponents<MudTextField<string>>();
        inputs.Should().HaveCount(3);
        inputs[0].Instance.Label.Should().Be("Current password");
        inputs[1].Instance.Label.Should().Be("New password");
        inputs[2].Instance.Label.Should().Be("Confirm new password");

        var buttons = cut.FindComponents<MudButton>();
        buttons.Should().HaveCount(3);
        cut.FindAll("button")[0].TextContent.Should().Contain("Change password");
        cut.FindAll("button")[1].TextContent.Should().Contain("Export data");
        cut.FindAll("button")[2].TextContent.Should().Contain("Delete account");
    }

    [Fact]
    public async Task ChangePassword_Validates_EmptyFields()
    {
        // Arrange
        var cut = _ctx.Render<AccountActionsSection>();
        var buttons = cut.FindComponents<MudButton>();

        // Act - Click Change Password with empty fields
        await cut.InvokeAsync(() => buttons[0].Instance.OnClick.InvokeAsync(null));

        // Assert
        await _notifier.Received(1).NotifyAsync("Enter your current password and a new password.", Severity.Warning);
        await _accountActions.DidNotReceiveWithAnyArgs().ChangePasswordAsync(null!, null!);
    }

    [Fact]
    public async Task ChangePassword_Validates_MismatchedPasswords()
    {
        // Arrange
        var cut = _ctx.Render<AccountActionsSection>();
        var inputs = cut.FindComponents<MudTextField<string>>();
        var buttons = cut.FindComponents<MudButton>();

        // Fill fields with mismatched new and confirm passwords
        await cut.InvokeAsync(() => inputs[0].Instance.ValueChanged.InvokeAsync("currentPass"));
        await cut.InvokeAsync(() => inputs[1].Instance.ValueChanged.InvokeAsync("newPass123"));
        await cut.InvokeAsync(() => inputs[2].Instance.ValueChanged.InvokeAsync("newPassMismatch"));

        // Act - Click Change Password
        await cut.InvokeAsync(() => buttons[0].Instance.OnClick.InvokeAsync(null));

        // Assert
        await _notifier.Received(1).NotifyAsync("New password confirmation does not match.", Severity.Warning);
        await _accountActions.DidNotReceiveWithAnyArgs().ChangePasswordAsync(null!, null!);
    }

    [Fact]
    public async Task ChangePassword_Success_CallsService_ClearsFields_AndNotifies()
    {
        // Arrange
        var cut = _ctx.Render<AccountActionsSection>();
        var inputs = cut.FindComponents<MudTextField<string>>();
        var buttons = cut.FindComponents<MudButton>();

        // Fill fields correctly
        await cut.InvokeAsync(() => inputs[0].Instance.ValueChanged.InvokeAsync("currentPass"));
        await cut.InvokeAsync(() => inputs[1].Instance.ValueChanged.InvokeAsync("newPass123"));
        await cut.InvokeAsync(() => inputs[2].Instance.ValueChanged.InvokeAsync("newPass123"));

        // Act - Click Change Password
        await cut.InvokeAsync(() => buttons[0].Instance.OnClick.InvokeAsync(null));

        // Assert
        await _accountActions.Received(1).ChangePasswordAsync("currentPass", "newPass123");
        await _notifier.Received(1).NotifyAsync("Password updated.", Severity.Success);

        // Fields should be cleared
        inputs[0].Instance.Value.Should().BeEmpty();
        inputs[1].Instance.Value.Should().BeEmpty();
        inputs[2].Instance.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task ChangePassword_Failure_ShowsErrorNotification()
    {
        // Arrange
        var cut = _ctx.Render<AccountActionsSection>();
        var inputs = cut.FindComponents<MudTextField<string>>();
        var buttons = cut.FindComponents<MudButton>();

        _accountActions.ChangePasswordAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(x => Task.FromException(new InvalidOperationException("Invalid current password")));

        // Fill fields correctly
        await cut.InvokeAsync(() => inputs[0].Instance.ValueChanged.InvokeAsync("wrongCurrent"));
        await cut.InvokeAsync(() => inputs[1].Instance.ValueChanged.InvokeAsync("newPass123"));
        await cut.InvokeAsync(() => inputs[2].Instance.ValueChanged.InvokeAsync("newPass123"));

        // Act - Click Change Password
        await cut.InvokeAsync(() => buttons[0].Instance.OnClick.InvokeAsync(null));

        // Assert
        await _notifier.Received(1).NotifyAsync("Invalid current password", Severity.Error);
    }

    [Fact]
    public async Task DeleteAccount_RequiresConfirmation_ThenCallsService_Notifies_AndNavigatesToWelcome()
    {
        // Arrange
        var cut = _ctx.Render<AccountActionsSection>();
        var buttons = cut.FindComponents<MudButton>();

        // Act - Click Delete Account, buttons[2]. Do not await: the handler waits on the confirmation dialog.
        var deleteClick = cut.InvokeAsync(() => buttons[2].Instance.OnClick.InvokeAsync(null));

        // Assert - confirmation dialog is shown and service is not called yet
        await _dialogProvider.WaitForStateAsync(() => _dialogProvider.Markup.Contains("Delete your account?"), TimeSpan.FromSeconds(5));
        await _accountActions.DidNotReceiveWithAnyArgs().DeleteAccountAsync(Arg.Any<CancellationToken>());

        // Act - Confirm deletion
        var confirmButton = _dialogProvider.FindAll("button").First(b => b.TextContent.Contains("Delete account"));
        await confirmButton.ClickAsync();
        await deleteClick;

        // Assert
        await _accountActions.Received(1).DeleteAccountAsync();
        await _notifier.Received(1).NotifyAsync("Your account has been deleted.", Severity.Success);

        var nav = _ctx.Services.GetRequiredService<NavigationManager>();
        nav.Uri.Should().Be("http://localhost/?accountDeleted=1");
    }

    [Fact]
    public async Task DeleteAccount_Cancel_DoesNotCallService()
    {
        // Arrange
        var cut = _ctx.Render<AccountActionsSection>();
        var buttons = cut.FindComponents<MudButton>();

        // Act - Click Delete Account, buttons[2], then cancel the confirmation
        var deleteClick = cut.InvokeAsync(() => buttons[2].Instance.OnClick.InvokeAsync(null));
        await _dialogProvider.WaitForStateAsync(() => _dialogProvider.Markup.Contains("Cancel"), TimeSpan.FromSeconds(5));
        var cancelButton = _dialogProvider.FindAll("button").First(b => b.TextContent.Contains("Cancel"));
        await cancelButton.ClickAsync();
        await deleteClick;

        // Assert
        await _accountActions.DidNotReceiveWithAnyArgs().DeleteAccountAsync(Arg.Any<CancellationToken>());
        _ctx.Services.GetRequiredService<NavigationManager>().Uri.Should().Be("http://localhost/");
    }

    [Fact]
    public async Task DeleteAccount_InvokesOnAccountDeleted_WhenProvided()
    {
        // Arrange
        var callbackInvoked = false;
        var cut = _ctx.Render<AccountActionsSection>(parameters => parameters
            .Add(x => x.OnAccountDeleted, () => callbackInvoked = true));
        var buttons = cut.FindComponents<MudButton>();

        // Act - Click Delete Account, buttons[2], then confirm
        var deleteClick = cut.InvokeAsync(() => buttons[2].Instance.OnClick.InvokeAsync(null));
        await _dialogProvider.WaitForStateAsync(() => _dialogProvider.Markup.Contains("Delete your account?"), TimeSpan.FromSeconds(5));
        var confirmButton = _dialogProvider.FindAll("button").First(b => b.TextContent.Contains("Delete account"));
        await confirmButton.ClickAsync();
        await deleteClick;

        // Assert
        await _accountActions.Received(1).DeleteAccountAsync();
        callbackInvoked.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAccount_Failure_ShowsErrorNotification()
    {
        // Arrange
        var cut = _ctx.Render<AccountActionsSection>();
        var buttons = cut.FindComponents<MudButton>();
        _accountActions.DeleteAccountAsync(Arg.Any<CancellationToken>())
            .Returns(x => Task.FromException(new InvalidOperationException("Account deletion failed.")));

        // Act - Click Delete Account, buttons[2], then confirm
        var deleteClick = cut.InvokeAsync(() => buttons[2].Instance.OnClick.InvokeAsync(null));
        await _dialogProvider.WaitForStateAsync(() => _dialogProvider.Markup.Contains("Delete your account?"), TimeSpan.FromSeconds(5));
        var confirmButton = _dialogProvider.FindAll("button").First(b => b.TextContent.Contains("Delete account"));
        await confirmButton.ClickAsync();
        await deleteClick;

        // Assert
        await _notifier.Received(1).NotifyAsync("Account deletion failed.", Severity.Error);
        _ctx.Services.GetRequiredService<NavigationManager>().Uri.Should().Be("http://localhost/");
    }

    [Fact]
    public async Task ExportData_CallsService_AndAttemptsDownload()
    {
        // Arrange
        var cut = _ctx.Render<AccountActionsSection>();
        var buttons = cut.FindComponents<MudButton>();

        // Act - Click Export Data, buttons[1]
        await cut.InvokeAsync(() => buttons[1].Instance.OnClick.InvokeAsync(null));

        // Assert
        await _export.Received(1).ExportAsync(Arg.Any<CancellationToken>());
        var jsLog = string.Join(";", _ctx.JSInterop.Invocations.Select(i => i.Identifier));
        jsLog.Should().Contain("habitinatorLoadScript");
    }
}
