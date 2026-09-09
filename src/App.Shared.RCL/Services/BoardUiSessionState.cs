namespace App.Shared.RCL.Services;

public sealed class BoardUiSessionState
{
    public string SearchText { get; set; } = string.Empty;
#pragma warning disable IDE0028
    public HashSet<string> SelectedFilterTags { get; } = new(StringComparer.OrdinalIgnoreCase);
#pragma warning restore IDE0028
    public int MobileSectionIndex { get; set; }
    public int SettingsSectionIndex { get; set; }
}
