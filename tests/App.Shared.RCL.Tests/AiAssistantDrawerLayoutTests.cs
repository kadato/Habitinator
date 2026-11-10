using App.Shared.RCL.Components;
using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

using Bunit;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using MudBlazor;
using MudBlazor.Services;

using NSubstitute;

namespace App.Shared.RCL.Tests;

public sealed class AiAssistantDrawerLayoutTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();
    private readonly IUserPreferencesService _preferences = Substitute.For<IUserPreferencesService>();
    private readonly IAiAssistantService _aiService = Substitute.For<IAiAssistantService>();
    private readonly IBoardDataService _boardData = Substitute.For<IBoardDataService>();
    private readonly IRemoteBoardRefreshService _remoteRefresh = Substitute.For<IRemoteBoardRefreshService>();
    private readonly IBoardSyncStatus _boardSync = Substitute.For<IBoardSyncStatus>();
    private readonly BoardUiSessionState _sessionState = new();

    public AiAssistantDrawerLayoutTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();
        _ctx.Services.AddSingleton(_sessionState);
        _ctx.Services.AddSingleton(_preferences);
        _ctx.Services.AddSingleton(_aiService);
        _ctx.Services.AddSingleton(_boardData);
        _ctx.Services.AddSingleton(_remoteRefresh);
        _ctx.Services.AddSingleton(_boardSync);

        var prefs = UserPreferences.CreateDefault();
        prefs.Ai.Enabled = true;
        prefs.Ai.Model = "muse-spark-1.3-contributor";
        _preferences.GetAsync(Arg.Any<CancellationToken>()).Returns(prefs);
        _boardData.GetSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(new BoardSnapshot([], [], []));

        _sessionState.IsAiDrawerOpen = true;

        _ = _ctx.Render<MudPopoverProvider>();
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    [Fact]
    public void Header_Pill_Shows_Full_Model_Name_Without_Brand_Label()
    {
        var cut = _ctx.Render<AiAssistantDrawer>();

        var pillName = cut.WaitForElement(".ai-model-pill__name");
        pillName.TextContent.Trim().Should().Be("Muse Spark 1.3 Contributor");

        cut.FindAll(".ai-header-title").Should().BeEmpty(
            "the Habitinator AI label was removed so the dropdown uses all header space");
    }

    [Fact]
    public void Assistant_Bubble_Has_No_Meta_Row_Model_Shown_Once_In_Header()
    {
        var cut = _ctx.Render<AiAssistantDrawer>();
        cut.WaitForElement(".ai-message-bubble");

        cut.FindAll(".ai-bubble-header").Should().BeEmpty("the per-message header was removed");
        cut.FindAll(".ai-bubble-model-pill").Should().BeEmpty("the model is shown once in the drawer header");
        cut.Find(".ai-rendered-markdown").Should().NotBeNull("the reply text still renders");

        var headerPill = cut.Find(".ai-model-pill__name");
        headerPill.TextContent.Trim().Should().Be("Muse Spark 1.3 Contributor");
    }

    [Fact]
    public async Task Message_Actions_Carry_Hidden_By_Default_Class()
    {
        _aiService.StreamChatAsync(Arg.Any<AiChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(System.Linq.AsyncEnumerable.Empty<AiChatStreamEvent>());

        var cut = _ctx.Render<AiAssistantDrawer>();
        await cut.WaitForElementAsync(".ai-message-bubble");

        await cut.Find(".ai-text-field textarea").InputAsync("Hello");
        await cut.Find(".ai-send-btn").ClickAsync();
        await cut.WaitForAssertionAsync(() => cut.FindAll(".ai-message--user").Should().HaveCount(1),
            TimeSpan.FromSeconds(5));

        var actionBars = cut.FindAll(".ai-message > .ai-message-actions");
        actionBars.Should().HaveCount(3, "greeting, user message, and reply each carry one action bar");
        cut.FindAll(".ai-message-bubble .ai-message-actions").Should().BeEmpty(
            "action bars live below the bubble, never over tiny message text");
    }
}
