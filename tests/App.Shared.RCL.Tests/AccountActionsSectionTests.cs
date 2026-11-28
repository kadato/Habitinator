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
    private readonly IUserDataImportService _import = Substitute.For<IUserDataImportService>();

    public AccountActionsSectionTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();
        _ctx.Services.AddSingleton<IBoardSyncStatus>(new NoOpBoardSyncStatus());
        _ctx.Services.AddSingleton<IAccountActionsService>(_accountActions);
        _ctx.Services.AddSingleton<IUserNotifier>(_notifier);
        _ctx.Services.AddSingleton<IUserDataExportService>(_export);
        _ctx.Services.AddSingleton<IUserDataImportService>(_import);
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
        buttons.Should().HaveCount(4);
        cut.FindAll("button")[0].TextContent.Should().Contain("Change password");
        cut.FindAll("button")[1].TextContent.Should().Contain("Export data");
        cut.FindAll("button")[2].TextContent.Should().Contain("Import data");
        cut.FindAll("button")[3].TextContent.Should().Contain("Delete account");
    }

    [Fact]
    public async Task ChangePassword_Validates_EmptyFields()
    {
        // Arrange
        var cut = _ctx.Render<AccountActionsSection>();
        var buttons = cut.FindComponents<MudButton>();

        var inputs = cut.FindComponents<MudTextField<string>>();

        // Act: Click Change Password with empty fields
        await cut.InvokeAsync(() => buttons[0].Instance.OnClick.InvokeAsync(null));

        // Assert
        inputs[0].Instance.Error.Should().BeTrue();
        inputs[0].Instance.ErrorText.Should().Be("Enter your current password.");
        inputs[1].Instance.Error.Should().BeTrue();
        inputs[1].Instance.ErrorText.Should().Be("Enter a new password.");
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

        // Act: click change password
        await cut.InvokeAsync(() => buttons[0].Instance.OnClick.InvokeAsync(null));

        // Assert
        inputs[2].Instance.Error.Should().BeTrue();
        inputs[2].Instance.ErrorText.Should().Be("Passwords do not match.");
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

        // Act: click change password
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
    public async Task ChangePassword_Failure_ShowsFieldLevelError()
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

        // Act: click change password
        await cut.InvokeAsync(() => buttons[0].Instance.OnClick.InvokeAsync(null));

        // Assert
        inputs[0].Instance.Error.Should().BeTrue();
        inputs[0].Instance.ErrorText.Should().Be("Invalid current password");
        inputs[1].Instance.Value.Should().Be("newPass123");
        await _notifier.DidNotReceive().NotifyAsync("Invalid current password", Arg.Any<Severity>());
    }

    [Fact]
    public async Task DeleteAccount_RequiresConfirmation_ThenCallsService_Notifies_AndNavigatesToWelcome()
    {
        // Arrange
        var cut = _ctx.Render<AccountActionsSection>();

        // Act: open the confirmation and make sure nothing is deleted yet
        await cut.InvokeAsync(() => DeleteAccountButton(cut).Click());
        await _accountActions.DidNotReceiveWithAnyArgs().DeleteAccountAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

        // Act: enter the password and confirm
        await EnterDeletePasswordAsync(cut, "secret");
        await cut.InvokeAsync(() => DeleteAccountButton(cut).Click());

        // Assert
        await _accountActions.Received(1).DeleteAccountAsync("secret");
        await _notifier.Received(1).NotifyAsync("Account deleted.", Severity.Success);

        var nav = _ctx.Services.GetRequiredService<NavigationManager>();
        nav.Uri.Should().Be("http://localhost/?accountDeleted=1");
    }

    [Fact]
    public async Task DeleteAccount_Cancel_DoesNotCallService()
    {
        // Arrange
        var cut = _ctx.Render<AccountActionsSection>();

        // Act: open the confirmation and cancel it
        await cut.InvokeAsync(() => DeleteAccountButton(cut).Click());
        await cut.InvokeAsync(() => cut.FindAll("button").Single(b => b.TextContent.Trim() == "Cancel").Click());

        // Assert
        await _accountActions.DidNotReceiveWithAnyArgs().DeleteAccountAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        cut.FindAll("[aria-label='Confirm account deletion']").Should().BeEmpty();
        _ctx.Services.GetRequiredService<NavigationManager>().Uri.Should().Be("http://localhost/");
    }

    [Fact]
    public async Task DeleteAccount_Rejects_An_Empty_Password()
    {
        // Arrange
        var cut = _ctx.Render<AccountActionsSection>();

        // Act: open the confirmation and press Delete without a password
        await cut.InvokeAsync(() => DeleteAccountButton(cut).Click());
        await cut.InvokeAsync(() => DeleteAccountButton(cut).Click());

        // Assert
        await _accountActions.DidNotReceiveWithAnyArgs().DeleteAccountAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        cut.Markup.Should().Contain("Enter your current password.");
    }

    [Fact]
    public async Task DeleteAccount_InvokesOnAccountDeleted_WhenProvided()
    {
        // Arrange
        var callbackInvoked = false;
        var cut = _ctx.Render<AccountActionsSection>(parameters => parameters
            .Add(x => x.OnAccountDeleted, () => callbackInvoked = true));

        // Act: open the confirmation, enter the password, and confirm
        await cut.InvokeAsync(() => DeleteAccountButton(cut).Click());
        await EnterDeletePasswordAsync(cut, "secret");
        await cut.InvokeAsync(() => DeleteAccountButton(cut).Click());

        // Assert
        await _accountActions.Received(1).DeleteAccountAsync("secret");
        callbackInvoked.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAccount_Failure_ShowsAnInlineError()
    {
        // Arrange
        var cut = _ctx.Render<AccountActionsSection>();
        _accountActions.DeleteAccountAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(x => Task.FromException(new InvalidOperationException("Account deletion failed.")));

        // Act: open the confirmation, enter the password, and confirm
        await cut.InvokeAsync(() => DeleteAccountButton(cut).Click());
        await EnterDeletePasswordAsync(cut, "secret");
        await cut.InvokeAsync(() => DeleteAccountButton(cut).Click());

        // Assert
        await cut.WaitForAssertionAsync(() => cut.Markup.Should().Contain("Account deletion failed."));
        await _notifier.DidNotReceive().NotifyAsync("Account deletion failed.", Severity.Error);
        _ctx.Services.GetRequiredService<NavigationManager>().Uri.Should().Be("http://localhost/");
    }

    private static AngleSharp.Dom.IElement DeleteAccountButton(IRenderedComponent<AccountActionsSection> cut) =>
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Delete account");

    private static Task EnterDeletePasswordAsync(IRenderedComponent<AccountActionsSection> cut, string password)
    {
        var inputs = cut.FindComponents<MudTextField<string>>();
        var input = inputs[^1];
        return cut.InvokeAsync(() => input.Instance.ValueChanged.InvokeAsync(password));
    }

    [Fact]
    public async Task ExportData_CallsService_AndAttemptsDownload()
    {
        // Arrange
        var cut = _ctx.Render<AccountActionsSection>();
        var buttons = cut.FindComponents<MudButton>();

        // Act: Click Export Data, buttons[1]
        await cut.InvokeAsync(() => buttons[1].Instance.OnClick.InvokeAsync(null));

        // Assert
        await _export.Received(1).ExportAsync(Arg.Any<CancellationToken>());
        var jsLog = string.Join(";", _ctx.JSInterop.Invocations.Select(i => i.Identifier));
        jsLog.Should().Contain("habitinatorLoadScript");
    }

    [Fact]
    public async Task ImportData_OpensImportDialog()
    {
        // Arrange
        var cut = _ctx.Render<AccountActionsSection>();
        var buttons = cut.FindComponents<MudButton>();

        // Act: Click Import Data, buttons[2]
        await cut.InvokeAsync(() => buttons[2].Instance.OnClick.InvokeAsync(null));

        // Assert
        await _dialogProvider.WaitForStateAsync(() => _dialogProvider.Markup.Contains("Load or paste a backup"), TimeSpan.FromSeconds(5));
        _dialogProvider.Markup.Should().Contain("Restore replaces everything");
    }
}
