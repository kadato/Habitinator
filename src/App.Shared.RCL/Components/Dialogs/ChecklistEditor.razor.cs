using Microsoft.AspNetCore.Components;

namespace App.Shared.RCL.Components.Dialogs;

public partial class ChecklistEditor
{
    [Parameter] public EventCallback<List<ChecklistRow>> ValueChanged { get; set; }
}
