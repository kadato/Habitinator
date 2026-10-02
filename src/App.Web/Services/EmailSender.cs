namespace App.Web.Services;

public interface IEmailSender
{
    Task SendPasswordResetAsync(string email, string resetLink, CancellationToken cancellationToken = default);
}

public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendPasswordResetAsync(string email, string resetLink, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Password reset requested for {Email}. Reset link: {ResetLink}", email, resetLink);
        return Task.CompletedTask;
    }
}
