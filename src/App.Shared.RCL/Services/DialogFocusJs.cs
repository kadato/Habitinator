using Microsoft.JSInterop;

namespace App.Shared.RCL.Services;

/// <summary>Shared helper that decides dialog autofocus. Touch devices and MAUI open dialogs unfocused.</summary>
public static class DialogFocusJs
{
    private const string ScriptPath = "_content/App.Shared.RCL/js/dialogFocus.js";

    public static async Task LoadAsync(IJSRuntime js)
    {
        await js.InvokeVoidAsync("habitinatorLoadScript", ScriptPath);
    }

    public static async Task<bool> ShouldAutoFocusAsync(IJSRuntime js)
    {
        await LoadAsync(js);
        return await js.InvokeAsync<bool>("HabitinatorDialogFocus.shouldAutoFocus");
    }
}
