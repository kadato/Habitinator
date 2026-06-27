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
    DateTimeOffset? ExpectedServerUpdatedAtUtc = null,
    double? SortOrder = null);

public sealed record UpdateTodoOutboxPayload(
    Guid ItemId,
    string Title,
    string? Notes,
    string? Tags,
    string? ChecklistJson,
    DateTime? DueDate,
    DateTimeOffset? ExpectedServerUpdatedAtUtc = null,
    double? SortOrder = null);

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
    DateTimeOffset? ExpectedServerUpdatedAtUtc = null,
    double? SortOrder = null);

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
            BoardOutboxOperationKind.Rename => RemapRenameClientId(payloadJson, clientId, serverId),
            BoardOutboxOperationKind.Delete or BoardOutboxOperationKind.Toggle => RemapSectionItemClientId(payloadJson, clientId, serverId),
            BoardOutboxOperationKind.CompleteDailyForDate => RemapCompleteDailyClientId(payloadJson, clientId, serverId),
            BoardOutboxOperationKind.HabitIncrement or BoardOutboxOperationKind.HabitDecrement => RemapItemIdClientId(payloadJson, clientId, serverId),
            BoardOutboxOperationKind.UpdateHabit => RemapUpdateHabitClientId(payloadJson, clientId, serverId),
            BoardOutboxOperationKind.UpdateTodo => RemapUpdateTodoClientId(payloadJson, clientId, serverId),
            BoardOutboxOperationKind.UpdateDaily => RemapUpdateDailyClientId(payloadJson, clientId, serverId),
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
            BoardOutboxOperationKind.Rename => RemapRenameVersion(payloadJson, newVersion),
            BoardOutboxOperationKind.Delete or BoardOutboxOperationKind.Toggle => RemapSectionItemVersion(payloadJson, newVersion),
            BoardOutboxOperationKind.CompleteDailyForDate => RemapCompleteDailyVersion(payloadJson, newVersion),
            BoardOutboxOperationKind.HabitIncrement or BoardOutboxOperationKind.HabitDecrement => RemapItemIdVersion(payloadJson, newVersion),
            BoardOutboxOperationKind.UpdateHabit => RemapUpdateHabitVersion(payloadJson, newVersion),
            BoardOutboxOperationKind.UpdateTodo => RemapUpdateTodoVersion(payloadJson, newVersion),
            BoardOutboxOperationKind.UpdateDaily => RemapUpdateDailyVersion(payloadJson, newVersion),
            _ => payloadJson
        };
    }

    private static Guid MapId(Guid id, Guid clientId, Guid serverId) => id == clientId ? serverId : id;

    private static string RemapRenameClientId(string json, Guid clientId, Guid serverId)
    {
        RenameOutboxPayload? p = JsonSerializer.Deserialize<RenameOutboxPayload>(json, BoardOutboxJson.Options);
        RenameOutboxPayload? updated = p is null ? null : p with { ItemId = MapId(p.ItemId, clientId, serverId) };
        return JsonSerializer.Serialize(updated, BoardOutboxJson.Options);
    }

    private static string RemapSectionItemClientId(string json, Guid clientId, Guid serverId)
    {
        SectionItemOutboxPayload? p = JsonSerializer.Deserialize<SectionItemOutboxPayload>(json, BoardOutboxJson.Options);
        SectionItemOutboxPayload? updated = p is null ? null : p with { ItemId = MapId(p.ItemId, clientId, serverId) };
        return JsonSerializer.Serialize(updated, BoardOutboxJson.Options);
    }

    private static string RemapCompleteDailyClientId(string json, Guid clientId, Guid serverId)
    {
        CompleteDailyOutboxPayload? p = JsonSerializer.Deserialize<CompleteDailyOutboxPayload>(json, BoardOutboxJson.Options);
        CompleteDailyOutboxPayload? updated = p is null ? null : p with { ItemId = MapId(p.ItemId, clientId, serverId) };
        return JsonSerializer.Serialize(updated, BoardOutboxJson.Options);
    }

    private static string RemapItemIdClientId(string json, Guid clientId, Guid serverId)
    {
        ItemIdOutboxPayload? p = JsonSerializer.Deserialize<ItemIdOutboxPayload>(json, BoardOutboxJson.Options);
        ItemIdOutboxPayload? updated = p is null ? null : p with { ItemId = MapId(p.ItemId, clientId, serverId) };
        return JsonSerializer.Serialize(updated, BoardOutboxJson.Options);
    }

    private static string RemapUpdateHabitClientId(string json, Guid clientId, Guid serverId)
    {
        UpdateHabitOutboxPayload? p = JsonSerializer.Deserialize<UpdateHabitOutboxPayload>(json, BoardOutboxJson.Options);
        UpdateHabitOutboxPayload? updated = p is null ? null : p with { ItemId = MapId(p.ItemId, clientId, serverId) };
        return JsonSerializer.Serialize(updated, BoardOutboxJson.Options);
    }

    private static string RemapUpdateTodoClientId(string json, Guid clientId, Guid serverId)
    {
        UpdateTodoOutboxPayload? p = JsonSerializer.Deserialize<UpdateTodoOutboxPayload>(json, BoardOutboxJson.Options);
        UpdateTodoOutboxPayload? updated = p is null ? null : p with { ItemId = MapId(p.ItemId, clientId, serverId) };
        return JsonSerializer.Serialize(updated, BoardOutboxJson.Options);
    }

    private static string RemapUpdateDailyClientId(string json, Guid clientId, Guid serverId)
    {
        UpdateDailyOutboxPayload? p = JsonSerializer.Deserialize<UpdateDailyOutboxPayload>(json, BoardOutboxJson.Options);
        UpdateDailyOutboxPayload? updated = p is null ? null : p with { ItemId = MapId(p.ItemId, clientId, serverId) };
        return JsonSerializer.Serialize(updated, BoardOutboxJson.Options);
    }

    private static string RemapRenameVersion(string json, DateTimeOffset newVersion)
    {
        RenameOutboxPayload? p = JsonSerializer.Deserialize<RenameOutboxPayload>(json, BoardOutboxJson.Options);
        RenameOutboxPayload? updated = p is null ? null : p with { ExpectedServerUpdatedAtUtc = newVersion };
        return JsonSerializer.Serialize(updated, BoardOutboxJson.Options);
    }

    private static string RemapSectionItemVersion(string json, DateTimeOffset newVersion)
    {
        SectionItemOutboxPayload? p = JsonSerializer.Deserialize<SectionItemOutboxPayload>(json, BoardOutboxJson.Options);
        SectionItemOutboxPayload? updated = p is null ? null : p with { ExpectedServerUpdatedAtUtc = newVersion };
        return JsonSerializer.Serialize(updated, BoardOutboxJson.Options);
    }

    private static string RemapCompleteDailyVersion(string json, DateTimeOffset newVersion)
    {
        CompleteDailyOutboxPayload? p = JsonSerializer.Deserialize<CompleteDailyOutboxPayload>(json, BoardOutboxJson.Options);
        CompleteDailyOutboxPayload? updated = p is null ? null : p with { ExpectedServerUpdatedAtUtc = newVersion };
        return JsonSerializer.Serialize(updated, BoardOutboxJson.Options);
    }

    private static string RemapItemIdVersion(string json, DateTimeOffset newVersion)
    {
        ItemIdOutboxPayload? p = JsonSerializer.Deserialize<ItemIdOutboxPayload>(json, BoardOutboxJson.Options);
        ItemIdOutboxPayload? updated = p is null ? null : p with { ExpectedServerUpdatedAtUtc = newVersion };
        return JsonSerializer.Serialize(updated, BoardOutboxJson.Options);
    }

    private static string RemapUpdateHabitVersion(string json, DateTimeOffset newVersion)
    {
        UpdateHabitOutboxPayload? p = JsonSerializer.Deserialize<UpdateHabitOutboxPayload>(json, BoardOutboxJson.Options);
        UpdateHabitOutboxPayload? updated = p is null ? null : p with { ExpectedServerUpdatedAtUtc = newVersion };
        return JsonSerializer.Serialize(updated, BoardOutboxJson.Options);
    }

    private static string RemapUpdateTodoVersion(string json, DateTimeOffset newVersion)
    {
        UpdateTodoOutboxPayload? p = JsonSerializer.Deserialize<UpdateTodoOutboxPayload>(json, BoardOutboxJson.Options);
        UpdateTodoOutboxPayload? updated = p is null ? null : p with { ExpectedServerUpdatedAtUtc = newVersion };
        return JsonSerializer.Serialize(updated, BoardOutboxJson.Options);
    }

    private static string RemapUpdateDailyVersion(string json, DateTimeOffset newVersion)
    {
        UpdateDailyOutboxPayload? p = JsonSerializer.Deserialize<UpdateDailyOutboxPayload>(json, BoardOutboxJson.Options);
        UpdateDailyOutboxPayload? updated = p is null ? null : p with { ExpectedServerUpdatedAtUtc = newVersion };
        return JsonSerializer.Serialize(updated, BoardOutboxJson.Options);
    }
}
