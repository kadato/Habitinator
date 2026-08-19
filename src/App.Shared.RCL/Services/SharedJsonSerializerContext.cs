using System.Text.Json.Serialization;

using App.Shared.RCL.Models;
using App.Shared.RCL.Services.Remote;

namespace App.Shared.RCL.Services;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ItemTitleRequest))]
[JsonSerializable(typeof(HabitUpdateRequest))]
[JsonSerializable(typeof(DailyUpdateRequest))]
[JsonSerializable(typeof(TodoUpdateRequest))]
[JsonSerializable(typeof(DailyCompleteForDateRequest))]
[JsonSerializable(typeof(RegisterRequest))]
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(LoginResponse))]
[JsonSerializable(typeof(ChangePasswordRequest))]
[JsonSerializable(typeof(NotificationSettings))]
[JsonSerializable(typeof(UserPreferences))]
[JsonSerializable(typeof(BoardItem))]
[JsonSerializable(typeof(BoardSnapshot))]
[JsonSerializable(typeof(BoardSyncDelta))]
[JsonSerializable(typeof(BoardSyncItem))]
[JsonSerializable(typeof(DailyChecklistItem))]
[JsonSerializable(typeof(ActivityLogRequest))]
[JsonSerializable(typeof(ActivityOverviewDto))]
[JsonSerializable(typeof(ActivityDashboardDto))]
[JsonSerializable(typeof(DailyContributionsViewDto))]
[JsonSerializable(typeof(HabitContributionsViewDto))]
[JsonSerializable(typeof(ActivityDayDetailDto))]
[JsonSerializable(typeof(CreateOutboxPayload))]
[JsonSerializable(typeof(RenameOutboxPayload))]
[JsonSerializable(typeof(SectionItemOutboxPayload))]
[JsonSerializable(typeof(CompleteDailyOutboxPayload))]
[JsonSerializable(typeof(ItemIdOutboxPayload))]
[JsonSerializable(typeof(UpdateHabitOutboxPayload))]
[JsonSerializable(typeof(UpdateTodoOutboxPayload))]
[JsonSerializable(typeof(UpdateDailyOutboxPayload))]
[JsonSerializable(typeof(AuthStatusDto))]
[JsonSerializable(typeof(UserDataExportDto))]
[JsonSerializable(typeof(BoardColumnFilterState))]
[JsonSerializable(typeof(Dictionary<Guid, int>))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(List<string>))]
public partial class SharedJsonSerializerContext : JsonSerializerContext
{
}
