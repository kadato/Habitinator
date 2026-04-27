namespace App.Shared.RCL.Models;

public sealed record RegisterRequest(string Email, string Password, string Timezone = "Europe/Budapest");

/// <summary>Outcome of <c>POST /api/auth/register</c> for MAUI and other API clients.</summary>
public sealed record RegistrationResult(bool Succeeded, IReadOnlyList<string>? ErrorDetails = null, string? OtherError = null)
{
    public string Message =>
        Succeeded
            ? string.Empty
            : (ErrorDetails is { Count: > 0 } ? string.Join(" ", ErrorDetails) : (OtherError ?? "Registration failed."));
}

public sealed record LoginRequest(string Email, string Password, bool RememberMe = false);

public sealed record LoginResponse(string AccessToken, string Email);
