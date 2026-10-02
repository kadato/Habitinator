#pragma warning disable MUD0012

using System.Text.Json;

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

public sealed class ImportDataDialogTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();
    private readonly IRenderedComponent<MudDialogProvider> _dialogProvider;
    private readonly IUserDataImportService _import = Substitute.For<IUserDataImportService>();
    private readonly IUserNotifier _notifier = Substitute.For<IUserNotifier>();

    public ImportDataDialogTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();
        _ctx.Services.AddSingleton(_import);
        _ctx.Services.AddSingleton(_notifier);
        _dialogProvider = _ctx.Render<MudDialogProvider>();
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    [Fact]
    public async Task Validate_Shows_Item_And_Event_Counts()
    {
        var dialog = await OpenDialogAsync();
        var json = JsonSerializer.Serialize(SampleExport(), JsonDefaults.Export);

        await dialog.InvokeAsync(() => dialog.FindComponent<MudTextField<string>>().Instance.ValueChanged.InvokeAsync(json));
        await ClickButtonAsync(dialog, "Validate");

        dialog.Markup.Should().Contain("1 items, 1 activity events");
    }

    [Fact]
    public async Task Validate_Rejects_NonJson()
    {
        var dialog = await OpenDialogAsync();

        await dialog.InvokeAsync(() => dialog.FindComponent<MudTextField<string>>().Instance.ValueChanged.InvokeAsync("not json"));
        await ClickButtonAsync(dialog, "Validate");

        dialog.Markup.Should().Contain("not valid JSON");
        await _import.DidNotReceiveWithAnyArgs().ImportAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Restore_Confirms_Then_Calls_Service_And_Notifies()
    {
        _import.ImportAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new UserDataImportResult(1, 1, false, false));
        var dialog = await OpenDialogAsync();
        var json = JsonSerializer.Serialize(SampleExport(), JsonDefaults.Export);

        await dialog.InvokeAsync(() => dialog.FindComponent<MudTextField<string>>().Instance.ValueChanged.InvokeAsync(json));
        await ClickButtonAsync(dialog, "Validate");

        var restoreClick = dialog.InvokeAsync(() => ClickButtonAsync(dialog, "Restore"));
        await _dialogProvider.WaitForStateAsync(() => _dialogProvider.Markup.Contains("Restore this backup?"), TimeSpan.FromSeconds(5));
        var confirmButton = _dialogProvider.FindAll("button").First(b => b.TextContent.Contains("Restore backup"));
        await confirmButton.ClickAsync();
        await restoreClick;

        await _import.Received(1).ImportAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _notifier.Received(1).NotifyAsync("Restored 1 items and 1 activity events.", Severity.Success);
    }

    private async Task<IRenderedComponent<ImportDataDialog>> OpenDialogAsync()
    {
        var dialogService = _ctx.Services.GetRequiredService<IDialogService>();
        await _dialogProvider.InvokeAsync(async () =>
            await dialogService.ShowAsync<ImportDataDialog>(string.Empty));
        return _dialogProvider.FindComponent<ImportDataDialog>();
    }

    private static async Task ClickButtonAsync(IRenderedComponent<ImportDataDialog> dialog, string label)
    {
        var button = dialog.FindAll("button").First(b => b.TextContent.Contains(label));
        await button.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
    }

    private static UserDataExportDto SampleExport() => new(
        DateTimeOffset.UtcNow,
        [new BoardSyncItem(BoardSection.Daily, new BoardItem(Guid.NewGuid(), "Gym", DailyStartDate: new DateOnly(2024, 1, 1)))],
        [new UserActivityEventRecord(DateTimeOffset.UtcNow, ActivityEventType.DailyComplete, null, null)]);
}
