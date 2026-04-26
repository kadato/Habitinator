using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

namespace App.MAUI.Services;

public sealed class RemoteBoardDataService : IBoardDataService
{
    private static readonly JsonSerializerOptions Serializer = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IHttpClientFactory _http;

    public RemoteBoardDataService(IHttpClientFactory http) =>
        _http = http;

    private HttpClient Client => _http.CreateClient("api");

    public async Task<BoardSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage res = await Client.GetAsync("api/board", cancellationToken);
        res.EnsureSuccessStatusCode();
        BoardSnapshot? s = await res.Content.ReadFromJsonAsync<BoardSnapshot>(Serializer, cancellationToken);
        return s ?? throw new InvalidOperationException("Empty board response.");
    }

    public async Task<BoardItem> CreateItemAsync(BoardSection section, string title, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage res = await Client.PostAsJsonAsync(
            $"api/board/{section}",
            new ItemTitleRequest(title),
            Serializer,
            cancellationToken);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<BoardItem>(Serializer, cancellationToken))!;
    }

    public async Task<BoardItem?> RenameItemAsync(BoardSection section, Guid itemId, string title, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage res = await Client.PutAsJsonAsync(
            $"api/board/{section}/{itemId}",
            new ItemTitleRequest(title),
            Serializer,
            cancellationToken);
        return await ReadBoardItemOrNullAsync(res, cancellationToken);
    }

    public async Task<bool> DeleteItemAsync(BoardSection section, Guid itemId, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage res = await Client.DeleteAsync($"api/board/{section}/{itemId}", cancellationToken);
        if (res.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        res.EnsureSuccessStatusCode();
        return true;
    }

    public async Task<BoardItem?> ToggleItemAsync(BoardSection section, Guid itemId, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage res = await Client.PostAsync($"api/board/{section}/{itemId}/toggle", null, cancellationToken);
        return await ReadBoardItemOrNullAsync(res, cancellationToken);
    }

    public async Task<BoardItem?> CompleteDailyForDateAsync(Guid itemId, DateOnly completedOn, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage res = await Client.PostAsJsonAsync(
            $"api/board/dailies/{itemId}/complete-for-date",
            new DailyCompleteForDateRequest(completedOn),
            Serializer,
            cancellationToken);
        return await ReadBoardItemOrNullAsync(res, cancellationToken);
    }

    public async Task<BoardItem?> IncrementHabitPlusAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage res = await Client.PostAsync($"api/board/habits/{itemId}/increment", null, cancellationToken);
        return await ReadBoardItemOrNullAsync(res, cancellationToken);
    }

    public async Task<BoardItem?> IncrementHabitMinusAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage res = await Client.PostAsync($"api/board/habits/{itemId}/decrement", null, cancellationToken);
        return await ReadBoardItemOrNullAsync(res, cancellationToken);
    }

    public async Task<BoardItem?> UpdateHabitAsync(
        Guid itemId,
        string title,
        string? notes,
        string? tags,
        bool trackPlus,
        bool trackMinus,
        HabitResetPeriod resetPeriod,
        int counter,
        int negativeCounter,
        string? checklistJson = null,
        CancellationToken cancellationToken = default)
    {
        var body = new HabitUpdateRequest(
            title,
            notes,
            tags,
            trackPlus,
            trackMinus,
            resetPeriod,
            counter,
            negativeCounter,
            checklistJson);
        using HttpResponseMessage res = await Client.PutAsJsonAsync($"api/board/habits/{itemId}", body, Serializer, cancellationToken);
        return await ReadBoardItemOrNullAsync(res, cancellationToken);
    }

    public async Task<BoardItem?> UpdateTodoAsync(
        Guid itemId,
        string title,
        string? notes,
        string? tags,
        string? checklistJson,
        DateTime? dueDate,
        CancellationToken cancellationToken = default)
    {
        var body = new TodoUpdateRequest(title, notes, tags, checklistJson, dueDate);
        using HttpResponseMessage res = await Client.PutAsJsonAsync($"api/board/todos/{itemId}", body, Serializer, cancellationToken);
        return await ReadBoardItemOrNullAsync(res, cancellationToken);
    }

    public async Task<BoardItem?> UpdateDailyAsync(
        Guid itemId,
        string title,
        string? notes,
        string? tags,
        DateTime? startDate,
        DailyRepeatType repeatType,
        int repeatInterval,
        string? checklistJson,
        int streak,
        CancellationToken cancellationToken = default)
    {
        var body = new DailyUpdateRequest(
            title,
            notes,
            tags,
            startDate,
            repeatType,
            repeatInterval,
            checklistJson,
            streak);
        using HttpResponseMessage res = await Client.PutAsJsonAsync($"api/board/dailies/{itemId}", body, Serializer, cancellationToken);
        return await ReadBoardItemOrNullAsync(res, cancellationToken);
    }

    private static async Task<BoardItem?> ReadBoardItemOrNullAsync(HttpResponseMessage res, CancellationToken cancellationToken)
    {
        if (res.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<BoardItem>(Serializer, cancellationToken);
    }
}
