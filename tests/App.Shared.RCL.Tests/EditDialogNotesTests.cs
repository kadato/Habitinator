#pragma warning disable MUD0012

using App.Shared.RCL.Components.Dialogs;

using Bunit;

using FluentAssertions;

using MudBlazor;
using MudBlazor.Services;

namespace App.Shared.RCL.Tests;

public sealed class EditDialogNotesTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();

    public EditDialogNotesTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();
        _ctx.Render<MudPopoverProvider>();
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    [Fact]
    public void EditDialogNotes_Renders_Notes_Value()
    {
        var notes = "Remember to hydrate";
        var cut = _ctx.Render<EditDialogNotes>(parameters => parameters
            .Add(p => p.Notes, notes));

        cut.Find("textarea").GetAttribute("value").Should().Be(notes);
        cut.Find(".hab-modal__notes-field").Should().NotBeNull();
        cut.Find(".hab-modal__notes").Should().NotBeNull();
    }

    [Fact]
    public void EditDialogNotes_Invokes_NotesChanged_On_Change()
    {
        string? changedValue = null;
        var cut = _ctx.Render<EditDialogNotes>(parameters => parameters
            .Add(p => p.Notes, "Initial")
            .Add(p => p.NotesChanged, val => changedValue = val));

        var textarea = cut.Find("textarea");
        textarea.Input("Updated notes");

        changedValue.Should().Be("Updated notes");
    }

    [Fact]
    public void EditDialogHeader_Does_Not_Contain_Notes_Field()
    {
        var cut = _ctx.Render<EditDialogHeader>(parameters => parameters
            .Add(p => p.Title, "Sample Title"));

        cut.FindAll(".hab-modal__notes-field").Should().BeEmpty();
        cut.FindAll("textarea").Should().BeEmpty();
    }
}
