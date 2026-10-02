using System.Net;

using App.Shared.RCL.Models;
using App.Web.Data;

using FluentAssertions;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace App.Web.IntegrationTests;

[Collection(nameof(IntegrationCollection))]
public sealed class PasswordResetIntegrationTests(PostgresWebAppFactory factory)
{
    [Fact]
    public async Task ForgotPassword_UnknownEmail_ReturnsOkWithoutEnumeration()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var res = await client.PostAsJsonAsync(
            "/api/auth/forgot-password",
            new ForgotPasswordRequest($"unknown-{Guid.NewGuid():N}@integration.test"));
        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ForgotPassword_KnownEmail_ReturnsOk()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var email = await RegisterAsync(client);

        using var res = await client.PostAsJsonAsync(
            "/api/auth/forgot-password",
            new ForgotPasswordRequest(email));
        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ResetPassword_WithValidToken_SignsInWithNewPassword()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var email = await RegisterAsync(client);
        const string newPassword = "NewPassword1!Aa";

        var token = await GenerateTokenAsync(email);
        using var reset = await client.PostAsJsonAsync(
            "/api/auth/reset-password",
            new ResetPasswordRequest(email, token, newPassword));
        reset.EnsureSuccessStatusCode();

        using var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(email, newPassword, RememberMe: false));
        login.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task ResetPassword_WithInvalidToken_ReturnsBadRequest()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var email = await RegisterAsync(client);

        using var reset = await client.PostAsJsonAsync(
            "/api/auth/reset-password",
            new ResetPasswordRequest(email, "invalid-token", "NewPassword1!Aa"));
        reset.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static async Task<string> RegisterAsync(HttpClient client)
    {
        var email = $"reset-{Guid.NewGuid():N}@integration.test";
        const string password = "TestUser1!Aa";
        (await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, password)))
            .EnsureSuccessStatusCode();
        return email;
    }

    private async Task<string> GenerateTokenAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync(email);
        user.Should().NotBeNull();
        return await users.GeneratePasswordResetTokenAsync(user);
    }
}
