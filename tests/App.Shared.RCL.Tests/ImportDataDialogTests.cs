#pragma warning disable MUD0012

using System.Text;

using App.Shared.RCL.Components.Dialogs;
using App.Shared.RCL.Services;
using App.Shared.RCL.Services.Board.Local;

using Bunit;

using FluentAssertions;

using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;

using MudBlazor;
using MudBlazor.Services;

using NSubstitute;

namespace App.Shared.RCL.Tests;

public sealed class ImportDataDialogTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();
    private readonly IUserDataImportService _import = Substitute.For<IUserDataImportService>();
    private readonly IUserNotifier _notifier = Substitute.For<IUserNotifier>();

    public ImportDataDialogTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();
        _ctx.Services.AddSingleton(_import);
        _ctx.Services.AddSingleton(_notifier);
        _ctx.Services.AddSingleton<IBoardSyncStatus>(new StubBoardSyncStatus());
        _ctx.Render<MudPopoverProvider>();
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    [Fact]
    public async Task Choosing_a_file_fills_the_backup_and_shows_a_preview()
    {
        var provider = await OpenDialogAsync();
        var json = """{"exportedAtUtc":"2026-01-01T00:00:00Z","items":[],"events":[]}""";

        await ChooseFileAsync(provider, new FakeBrowserFile("backup.json", Encoding.UTF8.GetBytes(json)));

        await provider.WaitForAssertionAsync(() =>
        {
            provider.Markup.Should().Contain("Loaded backup.json");
            provider.Markup.Should().Contain("0 items, 0 activity events");
        });
        provider.FindComponent<MudTextField<string>>().Instance.Value.Should().Be(json);
    }

    [Fact]
    public async Task An_oversized_file_is_rejected_without_reading_it()
    {
        var provider = await OpenDialogAsync();

        await ChooseFileAsync(provider, new FakeBrowserFile("huge.json", [], size: 5_000_001));

        await provider.WaitForAssertionAsync(() =>
        {
            provider.Markup.Should().Contain("Backup file is larger than 5 MB.");
            provider.Markup.Should().Contain("Loaded huge.json.");
        });
        provider.FindComponent<MudTextField<string>>().Instance.Value.Should().BeEmpty();
    }

    private async Task<IRenderedComponent<MudDialogProvider>> OpenDialogAsync()
    {
        var provider = _ctx.Render<MudDialogProvider>();
        var dialogService = _ctx.Services.GetRequiredService<IDialogService>();
        await provider.InvokeAsync(() => dialogService.ShowAsync<ImportDataDialog>(string.Empty));
        await provider.WaitForElementAsync("[data-dialog-label='Import data']");
        return provider;
    }

    private static async Task ChooseFileAsync(
        IRenderedComponent<MudDialogProvider> provider,
        IBrowserFile file)
    {
        var input = provider.FindComponent<InputFile>();
        var args = new InputFileChangeEventArgs([file]);
        await provider.InvokeAsync(() => input.Instance.OnChange.InvokeAsync(args));
    }

    private sealed class FakeBrowserFile(string name, byte[] content, long? size = null) : IBrowserFile
    {
        public string Name => name;

        public DateTimeOffset LastModified => DateTimeOffset.UtcNow;

        public long Size => size ?? content.Length;

        public string ContentType => "application/json";

        public Stream OpenReadStream(long maxAllowedSize = 512_000, CancellationToken cancellationToken = default) =>
            new MemoryStream(content);
    }

    private sealed class StubBoardSyncStatus : IBoardSyncStatus
    {
        public bool IsOffline { get; set; }

        public bool IsSyncing { get; set; }

        public bool IsPaused { get; set; }

        public DateTimeOffset? LastSyncedUtc { get; set; }

        public int PendingCount { get; set; }

        public string? SyncProblemMessage { get; set; }

        public IReadOnlyList<BoardSyncConflict> RecentConflicts { get; set; } = [];

#pragma warning disable CS0067
        public event EventHandler? Changed;
#pragma warning restore CS0067
    }
}
