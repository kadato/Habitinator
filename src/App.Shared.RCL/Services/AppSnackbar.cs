using App.Shared.RCL.Components;

using MudBlazor;

namespace App.Shared.RCL.Services;

/// <summary>Shared MudBlazor snackbar appearance and click-to-dismiss behavior.</summary>
public static class AppSnackbar
{
    public const string ToastTypeClass = "habitinator-toast";

    public static void Configure(SnackbarOptions config, int visibleStateDurationMs)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.VisibleStateDuration = visibleStateDurationMs;
        config.HideIcon = true;
        config.ShowCloseIcon = false;
        config.SnackbarTypeClass = ToastTypeClass;
        config.SnackbarVariant = Variant.Text;
    }

    public static Snackbar AddMessage(
        ISnackbar snackbar,
        string message,
        int visibleStateDurationMs,
        string? key = null)
    {
        ArgumentNullException.ThrowIfNull(snackbar);
        Snackbar? toast = null;
        toast = snackbar.Add<ToastContent>(
            new Dictionary<string, object>
            {
                [nameof(ToastContent.Message)] = message,
                [nameof(ToastContent.OnDismiss)] = new Func<Task>(() =>
                {
                    toast?.ForceClose();
                    return Task.CompletedTask;
                }),
            },
            Severity.Normal,
            config => Configure(config, visibleStateDurationMs),
            key);

        return toast ?? throw new InvalidOperationException("Failed to show snackbar.");
    }
}
