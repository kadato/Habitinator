using System.Text;
using System.Text.Json;

namespace App.MAUI.Services;

/// <summary>Reads unverified JWT payload fields for display only (e.g. email when SecureStorage has no copy).</summary>
internal static class JwtAccessTokenDisplayClaims
{
    public static string? TryGetEmail(string? jwt)
    {
        if (string.IsNullOrEmpty(jwt)) return null;

        var parts = jwt.Split('.');
        if (parts.Length < 2) return null;

        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            switch (payload.Length % 4)
            {
                case 2: payload += "=="; break;
                case 3: payload += "="; break;
            }

            var jsonBytes = Convert.FromBase64String(payload);
            var json = Encoding.UTF8.GetString(jsonBytes);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("email", out var e))
            {
                var s = e.GetString();
                return string.IsNullOrEmpty(s) ? null : s;
            }
        }
        catch
        {
        }

        return null;
    }
}
