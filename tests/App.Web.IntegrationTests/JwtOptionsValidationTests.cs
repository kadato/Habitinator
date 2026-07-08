using App.Web.Auth;
using App.Web.DependencyInjection;

using FluentAssertions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using NSubstitute;

namespace App.Web.IntegrationTests;

public class JwtOptionsValidationTests
{
    [Fact]
    public void AddWebOptions_InDevelopment_AllowsDefaultKey()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "Habitinator",
                ["Jwt:Audience"] = "HabitinatorClients",
                ["Jwt:SigningKey"] = "replace-with-long-random-key-change-in-production",
                ["Jwt:ExpirationMinutes"] = "60"
            })
            .Build();

        services.AddSingleton<IConfiguration>(configuration);

        var environment = Substitute.For<IWebHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Development);

        // Act
        services.AddWebOptions(configuration, environment);
        var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<IOptions<JwtOptions>>();
        var act = () => { _ = options.Value; };
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("replace-with-long-random-key-change-in-production")]
    [InlineData("replace-this-with-a-long-random-64-char-minimum-key")]
    [InlineData("some-other-replace-key-that-is-too-long-but-has-replace-in-it")]
    public void AddWebOptions_InProduction_ThrowsOptionsValidationException_ForDefaultOrReplaceKeys(string invalidKey)
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "Habitinator",
                ["Jwt:Audience"] = "HabitinatorClients",
                ["Jwt:SigningKey"] = invalidKey,
                ["Jwt:ExpirationMinutes"] = "60"
            })
            .Build();

        services.AddSingleton<IConfiguration>(configuration);

        var environment = Substitute.For<IWebHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Production);

        // Act
        services.AddWebOptions(configuration, environment);
        var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<IOptions<JwtOptions>>();
        var act = () => { _ = options.Value; };
        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*JWT SigningKey must be changed in non-development environments.*");
    }

    [Fact]
    public void AddWebOptions_InProduction_AllowsValidSecureKey()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "Habitinator",
                ["Jwt:Audience"] = "HabitinatorClients",
                ["Jwt:SigningKey"] = "super-secret-secure-signing-key-that-is-at-least-32-chars-long-012",
                ["Jwt:ExpirationMinutes"] = "60"
            })
            .Build();

        services.AddSingleton<IConfiguration>(configuration);

        var environment = Substitute.For<IWebHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Production);

        // Act
        services.AddWebOptions(configuration, environment);
        var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<IOptions<JwtOptions>>();
        var act = () => { _ = options.Value; };
        act.Should().NotThrow();
    }

    [Fact]
    public void AddWebOptions_KeyTooShort_ThrowsOptionsValidationException()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "Habitinator",
                ["Jwt:Audience"] = "HabitinatorClients",
                ["Jwt:SigningKey"] = "too-short",
                ["Jwt:ExpirationMinutes"] = "60"
            })
            .Build();

        services.AddSingleton<IConfiguration>(configuration);

        var environment = Substitute.For<IWebHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Development);

        // Act
        services.AddWebOptions(configuration, environment);
        var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<IOptions<JwtOptions>>();
        var act = () => { _ = options.Value; };
        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*JWT Signing Key must be at least 32 characters*");
    }
}
