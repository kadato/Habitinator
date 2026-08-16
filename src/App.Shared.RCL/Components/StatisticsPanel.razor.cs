#pragma warning disable S3881 // Dispose is implemented in the generated Razor part
using App.Shared.RCL.Services;

namespace App.Shared.RCL.Components;

public partial class StatisticsPanel
{
    private readonly Dictionary<Guid, Dictionary<(int Row, int Col), ActivityHeatmapCellDto>> _dailyCellIndices = [];
    private readonly Dictionary<Guid, Dictionary<(int Row, int Col), ActivityHeatmapCellDto>> _habitCellIndices = [];
}
