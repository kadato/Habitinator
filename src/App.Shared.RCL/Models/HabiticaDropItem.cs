namespace App.Shared.RCL.Models;

/// <summary>
/// Wrapper for <see cref="BoardItem"/> used by <see cref="MudBlazor.MudDropContainer{T}"/> reordering.
/// </summary>
public sealed class HabiticaDropItem
{
    public const string ListZoneId = "habitica-list";

    public required BoardItem Item { get; set; }

    public string Zone { get; set; } = ListZoneId;

    public int Order { get; set; }
}
