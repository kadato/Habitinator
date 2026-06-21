using System.Text.Json;

using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services;

/// <summary>Item ids touched by pending outbox operations (for merge: do not apply remote upsert/delete for these ids).</summary>
public static class BoardOutboxReferencedIds
{
    public static HashSet<Guid> CollectFromPayloads(IEnumerable<(BoardOutboxOperationKind Kind, string PayloadJson)> rows)
    {
        var set = new HashSet<Guid>();
        foreach (var (kind, json) in rows)
        {
            CollectOne(kind, json, set);
        }

        return set;
    }

    private static void CollectOne(BoardOutboxOperationKind kind, string json, HashSet<Guid> set)
    {
        switch (kind)
        {
            case BoardOutboxOperationKind.Create:
                if (JsonSerializer.Deserialize<CreateOutboxPayload>(json, BoardOutboxJson.Options) is { } c)
                {
                    set.Add(c.ClientItemId);
                }

                break;
            case BoardOutboxOperationKind.Rename:
                if (JsonSerializer.Deserialize<RenameOutboxPayload>(json, BoardOutboxJson.Options) is { } r)
                {
                    set.Add(r.ItemId);
                }

                break;
            case BoardOutboxOperationKind.Delete:
            case BoardOutboxOperationKind.Toggle:
                if (JsonSerializer.Deserialize<SectionItemOutboxPayload>(json, BoardOutboxJson.Options) is { } s)
                {
                    set.Add(s.ItemId);
                }

                break;
            case BoardOutboxOperationKind.CompleteDailyForDate:
                if (JsonSerializer.Deserialize<CompleteDailyOutboxPayload>(json, BoardOutboxJson.Options) is { } d)
                {
                    set.Add(d.ItemId);
                }

                break;
            case BoardOutboxOperationKind.HabitIncrement:
            case BoardOutboxOperationKind.HabitDecrement:
                if (JsonSerializer.Deserialize<ItemIdOutboxPayload>(json, BoardOutboxJson.Options) is { } i)
                {
                    set.Add(i.ItemId);
                }

                break;
            case BoardOutboxOperationKind.UpdateHabit:
                if (JsonSerializer.Deserialize<UpdateHabitOutboxPayload>(json, BoardOutboxJson.Options) is { } h)
                {
                    set.Add(h.ItemId);
                }

                break;
            case BoardOutboxOperationKind.UpdateTodo:
                if (JsonSerializer.Deserialize<UpdateTodoOutboxPayload>(json, BoardOutboxJson.Options) is { } t)
                {
                    set.Add(t.ItemId);
                }

                break;
            case BoardOutboxOperationKind.UpdateDaily:
                if (JsonSerializer.Deserialize<UpdateDailyOutboxPayload>(json, BoardOutboxJson.Options) is { } u)
                {
                    set.Add(u.ItemId);
                }

                break;
        }
    }
}
