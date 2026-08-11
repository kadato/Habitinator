using MudBlazor;

namespace App.Shared.RCL.Components;

public static class DialogDefaults
{
    public static DialogOptions SmallEditor { get; } = new()
    {
        MaxWidth = MaxWidth.Small,
        FullWidth = false,
        CloseButton = false,
        CloseOnEscapeKey = true,
        NoHeader = true
    };
}
