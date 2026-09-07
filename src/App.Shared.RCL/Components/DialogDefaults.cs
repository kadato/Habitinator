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

    public static DialogOptions Wide { get; } = new()
    {
        MaxWidth = MaxWidth.Medium,
        FullWidth = true,
        CloseButton = false,
        CloseOnEscapeKey = true,
        NoHeader = true
    };
}
