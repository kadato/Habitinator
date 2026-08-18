using System.Text.Json;

using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services;

/// <summary>Item ids touched by pending outbox operations. For merge, do not apply remote upsert/delete for these ids.</summary>
public static class BoardOutboxReferencedIds
{
    public static HashSet<Guid> CollectFromPayloads(IEnumerable<(BoardOutboxOperationKind Kind, string PayloadJson)> rows) =>
        rows.Select(r => ExtractItemId(r.Kind, r.PayloadJson))
            .Where(id => id.HasValue)
            .Select(id => id.GetValueOrDefault())
            .ToHashSet();

    private static Guid? ExtractItemId(BoardOutboxOperationKind kind, string json)
    {
        return kind switch
        {
            BoardOutboxOperationKind.Create => JsonSerializer.Deserialize<CreateOutboxPayload>(json, BoardOutboxJson.Options)?.ClientItemId,
            BoardOutboxOperationKind.Rename => JsonSerializer.Deserialize<RenameOutboxPayload>(json, BoardOutboxJson.Options)?.ItemId,
            BoardOutboxOperationKind.Delete or BoardOutboxOperationKind.Toggle or BoardOutboxOperationKind.Archive or BoardOutboxOperationKind.Unarchive => JsonSerializer.Deserialize<SectionItemOutboxPayload>(json, BoardOutboxJson.Options)?.ItemId,
            BoardOutboxOperationKind.CompleteDailyForDate => JsonSerializer.Deserialize<CompleteDailyOutboxPayload>(json, BoardOutboxJson.Options)?.ItemId,
            BoardOutboxOperationKind.HabitIncrement or BoardOutboxOperationKind.HabitDecrement => JsonSerializer.Deserialize<ItemIdOutboxPayload>(json, BoardOutboxJson.Options)?.ItemId,
            BoardOutboxOperationKind.UpdateHabit => JsonSerializer.Deserialize<UpdateHabitOutboxPayload>(json, BoardOutboxJson.Options)?.ItemId,
            BoardOutboxOperationKind.UpdateTodo => JsonSerializer.Deserialize<UpdateTodoOutboxPayload>(json, BoardOutboxJson.Options)?.ItemId,
            BoardOutboxOperationKind.UpdateDaily => JsonSerializer.Deserialize<UpdateDailyOutboxPayload>(json, BoardOutboxJson.Options)?.ItemId,
            _ => null
        };
    }
}
