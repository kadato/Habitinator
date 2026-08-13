using System.Security.Cryptography;
using System.Text;

using App.Shared.RCL.Components;

using MudBlazor;

namespace App.Shared.RCL.Services;

/// <summary>Shared MudBlazor snackbar appearance, severity types, and content de-duplication.</summary>
public static class AppSnackbar
{
    public const string ToastTypeClass = "habitinator-toast";

    /// <summary>CSS class added per severity so each toast type gets its own accent and tint.</summary>
    public static string SeverityClass(Severity severity) => severity switch
    {
        Severity.Success => "severity-success",
        Severity.Warning => "severity-warning",
        Severity.Error => "severity-error",
        _ => "severity-info"
    };

    public static void Configure(SnackbarOptions config, int visibleStateDurationMs, Severity severity = Severity.Normal)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.VisibleStateDuration = visibleStateDurationMs;
        config.HideIcon = true;
        config.ShowCloseIcon = false;
        config.SnackbarTypeClass = $"{ToastTypeClass} {SeverityClass(severity)}";
        config.SnackbarVariant = Variant.Text;
        config.DuplicatesBehavior = SnackbarDuplicatesBehavior.Prevent;
    }

    /// <summary>
    ///     Stable key derived from the message content. Identical notifications share a key so
    ///     <see cref="SnackbarDuplicatesBehavior.Prevent" /> collapses them into the toast already on screen.
    /// </summary>
    public static string MessageKey(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(message));
        return $"habitinator-msg-{Convert.ToHexString(hash)[..24]}";
    }

    /// <summary>
    ///     Shows a typed message toast. Returns <c>null</c> when a toast with the same content is already
    ///     visible. Duplicate content is collapsed, not stacked.
    /// </summary>
    public static Snackbar? AddMessage(
        ISnackbar snackbar,
        string message,
        int visibleStateDurationMs,
        Severity severity = Severity.Normal,
        string? key = null)
    {
        ArgumentNullException.ThrowIfNull(snackbar);
        key ??= MessageKey(message);
        Snackbar? toast = null;
        toast = snackbar.Add<ToastContent>(
            new Dictionary<string, object>
            {
                [nameof(ToastContent.Message)] = message,
                [nameof(ToastContent.Severity)] = severity,
                [nameof(ToastContent.OnDismiss)] = new Func<Task>(() =>
                {
                    toast?.ForceClose();
                    return Task.CompletedTask;
                }),
            },
            severity,
            config => Configure(config, visibleStateDurationMs, severity),
            key);

        return toast;
    }
}
