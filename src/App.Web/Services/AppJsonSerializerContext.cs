using System.Text.Json.Serialization;

using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using App.Shared.RCL.Services.Remote;

namespace App.Web.Services;

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
[JsonSerializable(typeof(CreateMcpTokenRequest))]
[JsonSerializable(typeof(CreateMcpTokenResponse))]
[JsonSerializable(typeof(ChangePasswordRequest))]
[JsonSerializable(typeof(NotificationSettings))]
[JsonSerializable(typeof(UserPreferences))]
[JsonSerializable(typeof(AiPreferences))]
[JsonSerializable(typeof(AiChatMessage))]
[JsonSerializable(typeof(List<AiChatMessage>))]
[JsonSerializable(typeof(AiToolCall))]
[JsonSerializable(typeof(List<AiToolCall>))]
[JsonSerializable(typeof(AiToolFunction))]
[JsonSerializable(typeof(AiToolExecution))]
[JsonSerializable(typeof(List<AiToolExecution>))]
[JsonSerializable(typeof(AiChatRequest))]
[JsonSerializable(typeof(AiChatResponse))]
[JsonSerializable(typeof(AiTestConnectionRequest))]
[JsonSerializable(typeof(AiTestConnectionResponse))]
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
[JsonSerializable(typeof(AuthStatusDto))]
[JsonSerializable(typeof(UserDataExportDto))]
[JsonSerializable(typeof(UserDataImportResult))]
[JsonSerializable(typeof(BoardColumnFilterState))]
[JsonSerializable(typeof(Dictionary<Guid, int>))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(List<string>))]
public partial class AppJsonSerializerContext : JsonSerializerContext
{
}
