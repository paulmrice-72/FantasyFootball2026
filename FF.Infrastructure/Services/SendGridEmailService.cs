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
    public async Task SendPasswordResetAsync(
    string toEmail,
    string firstName,
    string resetLink,
    CancellationToken ct = default)
    {
        var apiKey = configuration["SendGrid:ApiKey"];
        var fromEmail = configuration["SendGrid:FromEmail"];
        var fromName = configuration["SendGrid:FromName"];

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogWarning("SendGrid ApiKey not configured — skipping password reset email to {Email}", toEmail);
            return;
        }

        var htmlBody = $"""
        <div style="font-family:sans-serif;max-width:600px;margin:0 auto;">
            <h2 style="color:#F59E0B;">FantasyCombine.AI</h2>
            <p>Hey {firstName},</p>
            <p>We received a request to reset your password. Click the button below to set a new one:</p>
            <p style="margin:32px 0;">
                <a href="{resetLink}" 
                   style="background:#F59E0B;color:#000;padding:12px 24px;text-decoration:none;border-radius:4px;font-weight:bold;">
                    Reset Password
                </a>
            </p>
            <p>This link expires in 24 hours. If you didn't request a password reset, you can safely ignore this email.</p>
            <p style="color:#666;font-size:12px;">— The FantasyCombine.AI Team</p>
        </div>
        """;

        var client = new SendGridClient(apiKey);
        var from = new EmailAddress(fromEmail, fromName);
        var to = new EmailAddress(toEmail);
        var msg = MailHelper.CreateSingleEmail(from, to, "Reset your FantasyCombine.AI password", plainTextContent: null, htmlContent: htmlBody);

        var response = await client.SendEmailAsync(msg, ct);

        if ((int)response.StatusCode >= 400)
        {
            var body = await response.Body.ReadAsStringAsync(ct);
            logger.LogError(
                "SendGrid password reset failed for {Email} — Status {Status}: {Body}",
                toEmail, (int)response.StatusCode, body);
            throw new InvalidOperationException($"SendGrid failed: {(int)response.StatusCode}");
        }

        logger.LogInformation("Password reset email sent to {Email}", toEmail);
    }
}