#pragma warning disable S3881 // Dispose is implemented in the generated Razor part
namespace App.Shared.RCL.Components;

public partial class GlobalTimerPanel
{
    private Task<IEnumerable<string>> SearchSessionTargetsAsync(string value, CancellationToken cancellationToken)
    {
        return Task.FromResult(SearchSessionTargets(value));
    }
}
