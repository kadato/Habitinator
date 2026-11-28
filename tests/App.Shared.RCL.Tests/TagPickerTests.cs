using App.Shared.RCL.Components.Dialogs;
using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

using Bunit;

using FluentAssertions;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

using MudBlazor.Services;

using NSubstitute;

namespace App.Shared.RCL.Tests;

public sealed class TagPickerTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();
    private readonly IBoardDataService _boardData = Substitute.For<IBoardDataService>();

    public TagPickerTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();
        _ctx.Services.AddSingleton(_boardData);
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    [Fact]
    public void Arrow_Keys_Move_The_Active_Tag_And_Enter_Adds_It()
    {
        var changed = string.Empty;
        var habit = new BoardItem(Guid.NewGuid(), "Run", Tags: "health,home");
        var cut = _ctx.Render<TagPicker>(parameters => parameters
            .Add(p => p.Habits, [habit])
            .Add(p => p.Dailies, Array.Empty<BoardItem>())
            .Add(p => p.Todos, Array.Empty<BoardItem>())
            .Add(p => p.TagsChanged, EventCallback.Factory.Create<string>(this, value => changed = value)));

        cut.Find(".habit-tag-picker__control").Click();
        cut.WaitForState(() => cut.FindAll(".habit-tag-picker__option").Count > 0, TimeSpan.FromSeconds(5));

        cut.Find(".habit-tag-picker__search").Input("he");
        cut.WaitForAssertion(() => cut.FindAll(".habit-tag-picker__option").Count.Should().Be(2), TimeSpan.FromSeconds(5));

        cut.Find(".habit-tag-picker__search").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        cut.WaitForAssertion(
            () => cut.FindAll(".habit-tag-picker__option")[0].GetAttribute("aria-selected").Should().Be("true"),
            TimeSpan.FromSeconds(5));

        cut.Find(".habit-tag-picker__search").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        changed.Should().Be("health");
    }

    [Fact]
    public void First_Arrow_Up_Selects_The_Last_Option()
    {
        var habit = new BoardItem(Guid.NewGuid(), "Run", Tags: "health,home");
        var cut = _ctx.Render<TagPicker>(parameters => parameters
            .Add(p => p.Habits, [habit])
            .Add(p => p.Dailies, Array.Empty<BoardItem>())
            .Add(p => p.Todos, Array.Empty<BoardItem>()));

        cut.Find(".habit-tag-picker__control").Click();
        cut.WaitForState(() => cut.FindAll(".habit-tag-picker__option").Count > 0, TimeSpan.FromSeconds(5));

        cut.Find(".habit-tag-picker__search").KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });

        cut.WaitForAssertion(
            () => cut.FindAll(".habit-tag-picker__option")[^1].GetAttribute("aria-selected").Should().Be("true"),
            TimeSpan.FromSeconds(5));
    }
}
