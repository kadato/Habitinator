using MudBlazor;

namespace App.Shared.RCL.Components;

public static class DialogDefaults
{
    public static DialogOptions SmallEditor { get; } = new()
    {
        MaxWidth = MaxWidth.Small,
        FullWidth = true,
        CloseButton = false,
        CloseOnEscapeKey = true,
        NoHeader = true
    };

    /// <summary>Small editor for dialogs that confirm before discarding. The dialog handles Escape itself.</summary>
    public static DialogOptions SmallEditorNoEscape { get; } = new()
    {
        MaxWidth = MaxWidth.Small,
        FullWidth = true,
        CloseButton = false,
        CloseOnEscapeKey = false,
        NoHeader = true
    };

    public static DialogOptions Wide { get; } = new()
    {
        MaxWidth = MaxWidth.Medium,
        FullWidth = true,
        CloseButton = false,
        CloseOnEscapeKey = true,
        NoHeader = true
    };
}
