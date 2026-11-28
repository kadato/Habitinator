using System.Net;
using System.Text;
using System.Text.Json;

using App.Shared.RCL.Services.Remote;

using FluentAssertions;

using NSubstitute;

namespace App.Shared.RCL.Tests;

public sealed class RemoteAccountActionsServiceTests
{
    private static RemoteAccountActionsService CreateService(HttpStatusCode status, string body)
    {
        var handler = new StubHandler(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") });
        return new RemoteAccountActionsService(factory);
    }

    [Fact]
    public async Task ChangePassword_surfaces_the_server_detail()
    {
        var service = CreateService(
            HttpStatusCode.BadRequest,
            JsonSerializer.Serialize(new { detail = "Incorrect password." }));

        var act = () => service.ChangePasswordAsync("wrong", "NewPassword1!Aa");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Incorrect password.");
    }

    [Fact]
    public async Task ChangePassword_maps_a_short_new_password_validation_error()
    {
        var service = CreateService(
            HttpStatusCode.BadRequest,
            JsonSerializer.Serialize(new
            {
                title = "One or more validation errors occurred.",
                status = 400,
                errors = new Dictionary<string, string[]>
                {
                    ["NewPassword"] = ["The field NewPassword must be a string or array type with a minimum length of '8'."]
                }
            }));

        var act = () => service.ChangePasswordAsync("correct", "short");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("New password must be at least 8 characters.");
    }

    [Fact]
    public async Task DeleteAccount_surfaces_the_server_detail()
    {
        var service = CreateService(
            HttpStatusCode.BadRequest,
            JsonSerializer.Serialize(new { detail = "Current password is incorrect." }));

        var act = () => service.DeleteAccountAsync("wrong");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Current password is incorrect.");
    }

    [Fact]
    public async Task DeleteAccount_maps_a_required_password_validation_error()
    {
        var service = CreateService(
            HttpStatusCode.BadRequest,
            JsonSerializer.Serialize(new
            {
                title = "One or more validation errors occurred.",
                status = 400,
                errors = new Dictionary<string, string[]>
                {
                    ["Password"] = ["The Password field is required."]
                }
            }));

        var act = () => service.DeleteAccountAsync(string.Empty);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Enter your current password.");
    }

    [Fact]
    public async Task DeleteAccount_succeeds_on_no_content()
    {
        var service = CreateService(HttpStatusCode.NoContent, string.Empty);

        var act = () => service.DeleteAccountAsync("secret");

        await act.Should().NotThrowAsync();
    }

    private sealed class StubHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response);
    }
}
