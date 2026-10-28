using System.Text;
using System.Text.Json;

namespace App.MAUI.Services;

/// <summary>Reads unverified JWT payload fields for display only, e.g. email when SecureStorage has no copy.</summary>
internal static class JwtAccessTokenDisplayClaims
{
    public static string? TryGetEmail(string? jwt)
    {
        return TryGetStringClaim(jwt, "email");
    }

    public static DateTimeOffset? TryGetExpiryUtc(string? jwt)
    {
        var raw = TryGetNumericClaim(jwt, "exp");
        if (raw is null)
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds((long)raw.Value);
        }
        catch
        {
            return null;
        }
    }

    public static bool IsExpired(string? jwt, TimeSpan skew = default)
    {
        var exp = TryGetExpiryUtc(jwt);
        if (exp is null)
        {
            return false;
        }

        if (skew == default)
        {
            skew = TimeSpan.FromMinutes(1);
        }

        return DateTimeOffset.UtcNow + skew >= exp.Value;
    }

    private static string? TryGetStringClaim(string? jwt, string name)
    {
        var doc = TryParsePayload(jwt);
        if (doc is null)
        {
            return null;
        }

        using (doc)
        {
            if (doc.RootElement.TryGetProperty(name, out var e))
            {
                var s = e.GetString();
                return string.IsNullOrEmpty(s) ? null : s;
            }
        }

        return null;
    }

    private static double? TryGetNumericClaim(string? jwt, string name)
    {
        var doc = TryParsePayload(jwt);
        if (doc is null)
        {
            return null;
        }

        using (doc)
        {
            if (doc.RootElement.TryGetProperty(name, out var e) && e.TryGetDouble(out var v))
            {
                return v;
            }
        }

        return null;
    }

    private static JsonDocument? TryParsePayload(string? jwt)
    {
        if (string.IsNullOrEmpty(jwt))
        {
            return null;
        }

        var parts = jwt.Split('.');
        if (parts.Length < 2)
        {
            return null;
        }

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
            return JsonDocument.Parse(json);
        }
        catch
        {
            return null;
        }
    }
}
