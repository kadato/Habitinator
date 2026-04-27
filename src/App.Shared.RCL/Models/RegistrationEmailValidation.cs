using System.ComponentModel.DataAnnotations;

namespace App.Shared.RCL.Models;

/// <summary>Same rules as HTML <c>type="email"</c> + required — used where form validation does not run (MAUI, API).</summary>
public static class RegistrationEmailValidation
{
    private static readonly EmailAddressAttribute Validator = new();

    public static bool IsValid(string? email) =>
        !string.IsNullOrWhiteSpace(email) && Validator.IsValid(email);
}
