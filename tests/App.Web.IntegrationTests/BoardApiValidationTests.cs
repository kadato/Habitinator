using System.Net;
using System.Net.Http.Headers;

using App.Shared.RCL.Models;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;

namespace App.Web.IntegrationTests;

[Collection(nameof(IntegrationCollection))]
public sealed class BoardApiValidationTests(PostgresWebAppFactory factory)
{
    [Fact]
    public async Task CreateTodo_WithOverlongTitle_ReturnsBadRequest()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var suffix = Guid.NewGuid().ToString("N");
        var email = $"validate-{suffix}@integration.test";
        const string password = "TestUser1!Aa";

        (await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, password)))
            .IsSuccessStatusCode.Should().BeTrue();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password, RememberMe: false));
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/board/Todo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new ItemTitleRequest(new string('x', 201)));
        var res = await client.SendAsync(request);
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Register_WithInvalidEmail_ReturnsBadRequest()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var res = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("not-an-email", "TestUser1!Aa"));
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
