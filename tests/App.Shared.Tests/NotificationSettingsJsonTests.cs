using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

namespace App.Shared.Tests;

public sealed class NotificationSettingsJsonTests
{
    [Fact]
    public void Roundtrip_preserves_flags()
    {
        var original = new NotificationSettings
        {
            InAppMessagesEnabled = true,
            ShowSuccessToasts = false,
            ToastDuration = NotificationToastDuration.Long,
            DailyReminderEnabled = true,
            DailyReminderTime = TimeSpan.FromHours(8) + TimeSpan.FromMinutes(30),
            QuietHoursEnabled = true,
            QuietHoursStartUtc = TimeSpan.FromHours(22),
            QuietHoursEndUtc = TimeSpan.FromHours(6),
        };

        var json = NotificationSettingsJson.Serialize(original);
        var back = NotificationSettingsJson.DeserializeOrDefault(json);

        Assert.False(back.ShowSuccessToasts);
        Assert.Equal(NotificationToastDuration.Long, back.ToastDuration);
        Assert.True(back.DailyReminderEnabled);
        Assert.Equal(TimeSpan.FromHours(8) + TimeSpan.FromMinutes(30), back.DailyReminderTime);
        Assert.True(back.QuietHoursEnabled);
    }

    [Fact]
    public void Null_or_invalid_json_yields_defaults()
    {
        var a = NotificationSettingsJson.DeserializeOrDefault(null);
        Assert.True(a.InAppMessagesEnabled);

        var b = NotificationSettingsJson.DeserializeOrDefault("{ not json");
        Assert.True(b.InAppMessagesEnabled);
    }
}
