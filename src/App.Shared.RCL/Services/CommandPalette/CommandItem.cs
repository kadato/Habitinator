namespace App.Shared.RCL.Services.CommandPalette;

/// <summary>
///     Represents an executable action or navigation item in the command palette.
/// </summary>
public sealed record CommandItem(
    string Id,
    string Title,
    string? Subtitle,
    string Category,
    string? Icon,
    string? ShortcutBadge = null,
    Func<Task>? Action = null,
    Func<Task<List<CommandItem>>>? ChildrenProvider = null,
    IReadOnlyList<string>? Keywords = null,
    bool IsDanger = false);
