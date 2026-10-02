using System.Net;
using System.Net.Mail;

using Microsoft.Extensions.Options;

namespace App.Web.Services;

public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? From { get; set; }
}

public sealed class SmtpEmailSender(
    IOptions<SmtpOptions> options,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendPasswordResetAsync(string email, string resetLink, CancellationToken cancellationToken = default)
    {
        var smtp = options.Value;
        using var message = new MailMessage(
            smtp.From ?? "no-reply@habitinator",
            email,
            "Reset your Habitinator password",
            $"Reset your password by opening this link: {resetLink}\n\nIf you did not request this, ignore this email.");

        using var client = new SmtpClient(smtp.Host, smtp.Port)
        {
            EnableSsl = true,
            Credentials = string.IsNullOrWhiteSpace(smtp.Username)
                ? CredentialCache.DefaultNetworkCredentials
                : new NetworkCredential(smtp.Username, smtp.Password ?? string.Empty)
        };

        logger.LogInformation("Sending password reset email to {Email} via {Host}.", email, smtp.Host);
        await client.SendMailAsync(message, cancellationToken);
    }
}
