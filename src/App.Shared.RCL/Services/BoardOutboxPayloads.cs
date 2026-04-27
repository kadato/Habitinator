using System.Text.Json;
using System.Text.Json.Serialization;

using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services;

public static class BoardOutboxJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

public sealed record CreateOutboxPayload(BoardSection Section, string Title, Guid ClientItemId);

public sealed record RenameOutboxPayload(
    BoardSection Section,
    Guid ItemId,
    string Title,
    DateTimeOffset? ExpectedServerUpdatedAtUtc = null);

public sealed record SectionItemOutboxPayload(
    BoardSection Section,
    Guid ItemId,
    DateTimeOffset? ExpectedServerUpdatedAtUtc = null);

public sealed record CompleteDailyOutboxPayload(
    Guid ItemId,
    DateOnly CompletedOn,
    DateTimeOffset? ExpectedServerUpdatedAtUtc = null);

public sealed record ItemIdOutboxPayload(Guid ItemId, DateTimeOffset? ExpectedServerUpdatedAtUtc = null);

public sealed record UpdateHabitOutboxPayload(
    Guid ItemId,
    string Title,
    string? Notes,
    string? Tags,
    bool TrackPlus,
    bool TrackMinus,
    HabitResetPeriod ResetPeriod,
    int Counter,
    int NegativeCounter,
    string? ChecklistJson,
    DateTimeOffset? ExpectedServerUpdatedAtUtc = null);

public sealed record UpdateTodoOutboxPayload(
    Guid ItemId,
    string Title,
    string? Notes,
    string? Tags,
    string? ChecklistJson,
    DateTime? DueDate,
    DateTimeOffset? ExpectedServerUpdatedAtUtc = null);

public sealed record UpdateDailyOutboxPayload(
    Guid ItemId,
    string Title,
    string? Notes,
    string? Tags,
    DateTime? StartDate,
    DailyRepeatType RepeatType,
    int RepeatInterval,
    string? ChecklistJson,
    int Streak,
    DateTimeOffset? ExpectedServerUpdatedAtUtc = null);

public static class BoardOutboxPayloadMapper
{
    /// <summary>Rewrite payload JSON after a create is acknowledged with a new server id.</summary>
    public static string RemapClientToServerId(
        BoardOutboxOperationKind kind,
        string payloadJson,
        Guid clientId,
        Guid serverId)
    {
        if (clientId == serverId) return payloadJson;

        return kind switch
        {
            BoardOutboxOperationKind.Create => payloadJson,
            BoardOutboxOperationKind.Rename => JsonSerializer.Serialize(
                Rename(JsonSerializer.Deserialize<RenameOutboxPayload>(payloadJson, BoardOutboxJson.Options)),
                BoardOutboxJson.Options),
            BoardOutboxOperationKind.Delete or BoardOutboxOperationKind.Toggle => JsonSerializer.Serialize(
                SectionItem(JsonSerializer.Deserialize<SectionItemOutboxPayload>(payloadJson, BoardOutboxJson.Options)),
                BoardOutboxJson.Options),
            BoardOutboxOperationKind.CompleteDailyForDate => JsonSerializer.Serialize(
                CompleteDaily(JsonSerializer.Deserialize<CompleteDailyOutboxPayload>(payloadJson, BoardOutboxJson.Options)),
                BoardOutboxJson.Options),
            BoardOutboxOperationKind.HabitIncrement or BoardOutboxOperationKind.HabitDecrement => JsonSerializer.Serialize(
                ItemId(JsonSerializer.Deserialize<ItemIdOutboxPayload>(payloadJson, BoardOutboxJson.Options)),
                BoardOutboxJson.Options),
            BoardOutboxOperationKind.UpdateHabit => JsonSerializer.Serialize(
                UpdateHabit(JsonSerializer.Deserialize<UpdateHabitOutboxPayload>(payloadJson, BoardOutboxJson.Options)),
                BoardOutboxJson.Options),
            BoardOutboxOperationKind.UpdateTodo => JsonSerializer.Serialize(
                UpdateTodo(JsonSerializer.Deserialize<UpdateTodoOutboxPayload>(payloadJson, BoardOutboxJson.Options)),
                BoardOutboxJson.Options),
            BoardOutboxOperationKind.UpdateDaily => JsonSerializer.Serialize(
                UpdateDaily(JsonSerializer.Deserialize<UpdateDailyOutboxPayload>(payloadJson, BoardOutboxJson.Options)),
                BoardOutboxJson.Options),
            _ => payloadJson
        };

        RenameOutboxPayload? Rename(RenameOutboxPayload? p) =>
            p is null ? null : p with { ItemId = Map(p.ItemId) };

        SectionItemOutboxPayload? SectionItem(SectionItemOutboxPayload? p) =>
            p is null ? null : p with { ItemId = Map(p.ItemId) };

        CompleteDailyOutboxPayload? CompleteDaily(CompleteDailyOutboxPayload? p) =>
            p is null ? null : p with { ItemId = Map(p.ItemId) };

        ItemIdOutboxPayload? ItemId(ItemIdOutboxPayload? p) =>
            p is null ? null : p with { ItemId = Map(p.ItemId) };

        UpdateHabitOutboxPayload? UpdateHabit(UpdateHabitOutboxPayload? p) =>
            p is null ? null : p with { ItemId = Map(p.ItemId) };

        UpdateTodoOutboxPayload? UpdateTodo(UpdateTodoOutboxPayload? p) =>
            p is null ? null : p with { ItemId = Map(p.ItemId) };

        UpdateDailyOutboxPayload? UpdateDaily(UpdateDailyOutboxPayload? p) =>
            p is null ? null : p with { ItemId = Map(p.ItemId) };

        Guid Map(Guid id) => id == clientId ? serverId : id;
    }
}
