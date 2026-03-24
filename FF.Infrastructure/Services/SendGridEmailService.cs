// FF.Infrastructure/Services/SendGridEmailService.cs
using FF.Application.Interfaces;
using FF.Application.Interfaces.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace FF.Infrastructure.Services;

public class SendGridEmailService(
    IConfiguration configuration,
    ILogger<SendGridEmailService> logger) : IEmailService
{
    public async Task SendWarRoomBriefAsync(
        string toEmail,
        string subject,
        string htmlBody,
        CancellationToken ct = default)
    {
        var apiKey = configuration["SendGrid:ApiKey"];
        var fromEmail = configuration["SendGrid:FromEmail"];
        var fromName = configuration["SendGrid:FromName"];

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogWarning("SendGrid ApiKey not configured — skipping email to {Email}", toEmail);
            return;
        }

        var client = new SendGridClient(apiKey);
        var from = new EmailAddress(fromEmail, fromName);
        var to = new EmailAddress(toEmail);
        var msg = MailHelper.CreateSingleEmail(from, to, subject, plainTextContent: null, htmlContent: htmlBody);

        var response = await client.SendEmailAsync(msg, ct);

        if ((int)response.StatusCode >= 400)
        {
            var body = await response.Body.ReadAsStringAsync(ct);
            logger.LogError(
                "SendGrid delivery failed for {Email} — Status {Status}: {Body}",
                toEmail, (int)response.StatusCode, body);
            throw new InvalidOperationException(
                $"SendGrid failed: {(int)response.StatusCode}");
        }

        logger.LogInformation("War Room Brief sent to {Email}", toEmail);
    }
}