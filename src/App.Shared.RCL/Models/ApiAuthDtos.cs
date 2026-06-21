using System.ComponentModel.DataAnnotations;

namespace App.Shared.RCL.Models;

public sealed record RegisterRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password);

/// <summary>Outcome of <c>POST /api/auth/register</c> for MAUI and other API clients.</summary>
public sealed record RegistrationResult(bool Succeeded, IReadOnlyList<string>? ErrorDetails = null, string? OtherError = null)
{
    public string Message =>
        Succeeded
            ? string.Empty
            : (ErrorDetails is { Count: > 0 } ? string.Join(" ", ErrorDetails) : (OtherError ?? "Registration failed."));
}

public sealed record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password,
    bool RememberMe = false);

public sealed record LoginResponse(string AccessToken, string Email);

