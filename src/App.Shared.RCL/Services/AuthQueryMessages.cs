using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.WebUtilities;

namespace App.Shared.RCL.Services;

/// <summary>Maps auth-page redirect query strings to user-facing messages, shared by the web and MAUI hosts.</summary>
public static class AuthQueryMessages
{
    public static string? Resolve(NavigationManager navigation)
    {
        var query = QueryHelpers.ParseQuery(navigation.ToAbsoluteUri(navigation.Uri).Query);
        if (query.TryGetValue("registered", out var registered) && registered == "1")
        {
            return "Registration complete. You can sign in now.";
        }

        if (query.TryGetValue("error", out var error) && error == "1")
        {
            return "Invalid email or password.";
        }

        if (query.TryGetValue("guest", out var guest) && guest == "missing")
        {
            return "Demo guest user is not available.";
        }

        return null;
    }

    public static AuthRegisterQuery ResolveRegister(NavigationManager navigation)
    {
        var query = QueryHelpers.ParseQuery(navigation.ToAbsoluteUri(navigation.Uri).Query);
        var email = query.TryGetValue("email", out var emailValue) && !string.IsNullOrWhiteSpace(emailValue)
            ? emailValue.ToString()
            : null;
        var message = query.TryGetValue("error", out var error) && error == "1"
            ? "Registration could not be completed. Check the password length, or try another email if this one is already registered."
            : null;

        return new AuthRegisterQuery(email, message);
    }
}

public sealed record AuthRegisterQuery(string? Email, string? Message);
