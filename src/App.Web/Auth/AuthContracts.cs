namespace App.Web.Auth;

public sealed record RegisterRequest(string Email, string Password, string Timezone = "Europe/Budapest");

public sealed record LoginRequest(string Email, string Password, bool RememberMe = false);

public sealed record LoginResponse(string AccessToken, string Email);
