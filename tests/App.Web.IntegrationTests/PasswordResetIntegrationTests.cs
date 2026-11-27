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

    [Fact]
    public async Task ForgotPasswordPage_PostsToServerFormEndpoint()
    {
        var client = factory.CreateClient();
        var html = await client.GetStringAsync("/auth/forgot-password");

        html.Should().Contain("action=\"/api/auth/forgot-password-form\"");
        html.Should().NotContain("__internal_preventDefault_onsubmit");
    }

    [Fact]
    public async Task ResetPasswordPage_PostsTokenAndNewPasswordToServer()
    {
        var client = factory.CreateClient();
        var html = await client.GetStringAsync("/auth/reset-password?email=user%40example.com&token=test-token");

        html.Should().Contain("action=\"/api/auth/reset-password-form\"");
        html.Should().Contain("name=\"NewPassword\"");
        html.Should().Contain("name=\"Token\" value=\"test-token\"");
    }

    [Fact]
    public async Task ForgotPasswordForm_RedirectsToSentEvenForUnknownEmail()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var res = await client.PostAsync(
            "/api/auth/forgot-password-form",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Email"] = $"unknown-{Guid.NewGuid():N}@integration.test"
            }));

        res.StatusCode.Should().Be(HttpStatusCode.Redirect);
        res.Headers.Location!.ToString().Should().Be("/auth/forgot-password?sent=1");
    }

    [Fact]
    public async Task ResetPasswordForm_WithValidToken_RedirectsToLoginAndWorks()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var email = await RegisterAsync(client);
        const string newPassword = "NewPassword1!Aa";
        var token = await GenerateTokenAsync(email);

        using var res = await client.PostAsync(
            "/api/auth/reset-password-form",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Email"] = email,
                ["Token"] = token,
                ["NewPassword"] = newPassword
            }));

        res.StatusCode.Should().Be(HttpStatusCode.Redirect);
        res.Headers.Location!.ToString().Should().Be("/auth/login?reset=1");

        using var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(email, newPassword, RememberMe: false));
        login.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task ResetPasswordForm_WithInvalidToken_RedirectsToError()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var email = await RegisterAsync(client);

        using var res = await client.PostAsync(
            "/api/auth/reset-password-form",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Email"] = email,
                ["Token"] = "invalid-token",
                ["NewPassword"] = "NewPassword1!Aa"
            }));

        res.StatusCode.Should().Be(HttpStatusCode.Redirect);
        res.Headers.Location!.ToString().Should().StartWith("/auth/reset-password?error=1");
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
