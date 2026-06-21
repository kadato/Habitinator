using System.Text.Json.Serialization;

using App.Shared.RCL.Models;
using App.Web.Models;

namespace App.Web.Services;

[JsonSerializable(typeof(ItemTitleRequest))]
[JsonSerializable(typeof(HabitUpdateRequest))]
[JsonSerializable(typeof(DailyUpdateRequest))]
[JsonSerializable(typeof(TodoUpdateRequest))]
[JsonSerializable(typeof(BoardSectionRequest))]
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
public partial class AppJsonSerializerContext : JsonSerializerContext
{
}
