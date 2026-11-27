using System.Security.Cryptography;

using Microsoft.AspNetCore.DataProtection;

namespace App.Web.Services;

/// <summary>
///     Encrypts provider API keys before they reach the preferences JSONB column.
///     Legacy plaintext values are still readable so existing accounts keep working
///     until their next save re-encrypts the key.
/// </summary>
public sealed class AiApiKeyProtector
{
    private const string Prefix = "dp:v1:";
    private readonly ILogger<AiApiKeyProtector> _logger;
    private readonly IDataProtector _protector;

    public AiApiKeyProtector(
        IDataProtectionProvider dataProtectionProvider,
        ILogger<AiApiKeyProtector> logger)
    {
        _logger = logger;
        _protector = dataProtectionProvider.CreateProtector("Habitinator.AiApiKey.v1");
    }

    public string Protect(string? apiKey)
    {
        var trimmed = apiKey?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        if (IsProtectedValue(trimmed))
        {
            return trimmed;
        }

        return Prefix + _protector.Protect(trimmed);
    }

    public string Unprotect(string? storedValue)
    {
        var trimmed = storedValue?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || !IsProtectedValue(trimmed))
        {
            return trimmed;
        }

        try
        {
            return _protector.Unprotect(trimmed[Prefix.Length..]);
        }
        catch (CryptographicException ex)
        {
            // A rotated or lost key ring cannot decrypt old values. Fail closed and
            // let the user enter the key again instead of sending garbage upstream.
            _logger.LogWarning(ex, "Stored AI API key could not be decrypted. The user must enter it again.");
            return string.Empty;
        }
    }

    public static bool IsProtectedValue(string? value) =>
        value is not null && value.StartsWith(Prefix, StringComparison.Ordinal);
}
