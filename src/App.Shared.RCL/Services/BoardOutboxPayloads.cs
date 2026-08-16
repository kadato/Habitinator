using System.Text.Json;

using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services;

public static class BoardOutboxJson
{
    /// <summary>Shared API JSON options used to serialize outbox payloads.</summary>
    public static JsonSerializerOptions Options => JsonDefaults.Api;
}

internal interface IOutboxItemIdPayload
{
    Guid ItemId { get; }
}

public sealed record CreateOutboxPayload(BoardSection Section, string Title, Guid ClientItemId);

public sealed record RenameOutboxPayload(
    BoardSection Section,
    Guid ItemId,
    string Title,
    DateTimeOffset? ExpectedServerUpdatedAtUtc = null) : IOutboxItemIdPayload;

public sealed record SectionItemOutboxPayload(
    BoardSection Section,
    Guid ItemId,
    DateTimeOffset? ExpectedServerUpdatedAtUtc = null) : IOutboxItemIdPayload;

public sealed record CompleteDailyOutboxPayload(
    Guid ItemId,
    DateOnly CompletedOn,
    DateTimeOffset? ExpectedServerUpdatedAtUtc = null) : IOutboxItemIdPayload;

public sealed record ItemIdOutboxPayload(Guid ItemId, DateTimeOffset? ExpectedServerUpdatedAtUtc = null) : IOutboxItemIdPayload;

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
    DateTimeOffset? ExpectedServerUpdatedAtUtc = null,
    double? SortOrder = null) : IOutboxItemIdPayload;

public sealed record UpdateTodoOutboxPayload(
    Guid ItemId,
    string Title,
    string? Notes,
    string? Tags,
    string? ChecklistJson,
    DateOnly? DueDate,
    DateTimeOffset? ExpectedServerUpdatedAtUtc = null,
    double? SortOrder = null,
    int? TodoRepeatIntervalDays = null) : IOutboxItemIdPayload;

public sealed record UpdateDailyOutboxPayload(
    Guid ItemId,
    string Title,
    string? Notes,
    string? Tags,
    DateOnly? StartDate,
    DailyRepeatType Repeat,
    int RepeatInterval,
    string? ChecklistJson,
    int Counter,
    DateTimeOffset? ExpectedServerUpdatedAtUtc = null,
    double? SortOrder = null) : IOutboxItemIdPayload;

public static class BoardOutboxPayloadMapper
{
    /// <summary>Rewrite payload JSON after a create is acknowledged with a new server id.</summary>
    public static string RemapClientToServerId(
        BoardOutboxOperationKind kind,
        string payloadJson,
        Guid clientId,
        Guid serverId)
    {
        if (clientId == serverId)
        {
            return payloadJson;
        }

        return kind switch
        {
            BoardOutboxOperationKind.Create => payloadJson,
            BoardOutboxOperationKind.Rename => RemapItemId(payloadJson, clientId, serverId, (RenameOutboxPayload p, Guid id) => p with { ItemId = id }),
            BoardOutboxOperationKind.Delete or BoardOutboxOperationKind.Toggle or BoardOutboxOperationKind.Archive or BoardOutboxOperationKind.Unarchive => RemapItemId(payloadJson, clientId, serverId, (SectionItemOutboxPayload p, Guid id) => p with { ItemId = id }),
            BoardOutboxOperationKind.CompleteDailyForDate => RemapItemId(payloadJson, clientId, serverId, (CompleteDailyOutboxPayload p, Guid id) => p with { ItemId = id }),
            BoardOutboxOperationKind.HabitIncrement or BoardOutboxOperationKind.HabitDecrement => RemapItemId(payloadJson, clientId, serverId, (ItemIdOutboxPayload p, Guid id) => p with { ItemId = id }),
            BoardOutboxOperationKind.UpdateHabit => RemapItemId(payloadJson, clientId, serverId, (UpdateHabitOutboxPayload p, Guid id) => p with { ItemId = id }),
            BoardOutboxOperationKind.UpdateTodo => RemapItemId(payloadJson, clientId, serverId, (UpdateTodoOutboxPayload p, Guid id) => p with { ItemId = id }),
            BoardOutboxOperationKind.UpdateDaily => RemapItemId(payloadJson, clientId, serverId, (UpdateDailyOutboxPayload p, Guid id) => p with { ItemId = id }),
            _ => payloadJson
        };
    }

    /// <summary>Rewrite payload JSON to update the ExpectedServerUpdatedAtUtc timestamp.</summary>
    public static string RemapExpectedVersion(
        BoardOutboxOperationKind kind,
        string payloadJson,
        DateTimeOffset newVersion)
    {
        return kind switch
        {
            BoardOutboxOperationKind.Rename => RemapExpectedVersion(payloadJson, newVersion, (RenameOutboxPayload p, DateTimeOffset? v) => p with { ExpectedServerUpdatedAtUtc = v }),
            BoardOutboxOperationKind.Delete or BoardOutboxOperationKind.Toggle or BoardOutboxOperationKind.Archive or BoardOutboxOperationKind.Unarchive => RemapExpectedVersion(payloadJson, newVersion, (SectionItemOutboxPayload p, DateTimeOffset? v) => p with { ExpectedServerUpdatedAtUtc = v }),
            BoardOutboxOperationKind.CompleteDailyForDate => RemapExpectedVersion(payloadJson, newVersion, (CompleteDailyOutboxPayload p, DateTimeOffset? v) => p with { ExpectedServerUpdatedAtUtc = v }),
            BoardOutboxOperationKind.HabitIncrement or BoardOutboxOperationKind.HabitDecrement => RemapExpectedVersion(payloadJson, newVersion, (ItemIdOutboxPayload p, DateTimeOffset? v) => p with { ExpectedServerUpdatedAtUtc = v }),
            BoardOutboxOperationKind.UpdateHabit => RemapExpectedVersion(payloadJson, newVersion, (UpdateHabitOutboxPayload p, DateTimeOffset? v) => p with { ExpectedServerUpdatedAtUtc = v }),
            BoardOutboxOperationKind.UpdateTodo => RemapExpectedVersion(payloadJson, newVersion, (UpdateTodoOutboxPayload p, DateTimeOffset? v) => p with { ExpectedServerUpdatedAtUtc = v }),
            BoardOutboxOperationKind.UpdateDaily => RemapExpectedVersion(payloadJson, newVersion, (UpdateDailyOutboxPayload p, DateTimeOffset? v) => p with { ExpectedServerUpdatedAtUtc = v }),
            _ => payloadJson
        };
    }

    private static Guid MapId(Guid id, Guid clientId, Guid serverId) => id == clientId ? serverId : id;

    private static string RemapItemId<T>(string json, Guid clientId, Guid serverId, Func<T, Guid, T> withItemId)
        where T : class, IOutboxItemIdPayload
    {
        var p = JsonSerializer.Deserialize<T>(json, BoardOutboxJson.Options);
        var updated = p is null ? null : withItemId(p, MapId(p.ItemId, clientId, serverId));
        return JsonSerializer.Serialize(updated, BoardOutboxJson.Options);
    }

    private static string RemapExpectedVersion<T>(string json, DateTimeOffset newVersion, Func<T, DateTimeOffset?, T> withVersion)
        where T : class, IOutboxItemIdPayload
    {
        var p = JsonSerializer.Deserialize<T>(json, BoardOutboxJson.Options);
        var updated = p is null ? null : withVersion(p, newVersion);
        return JsonSerializer.Serialize(updated, BoardOutboxJson.Options);
    }
}
