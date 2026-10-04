using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;
using Saakh.Api.Configuration;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace Saakh.Api.Services;

public interface IEmailSender
{
    Task SendAsync(string toAddress, string toName, string subject, string textBody,
        CancellationToken ct = default);
}

/// <summary>Development default — no real inbox is ever touched by a test run.</summary>
public class LoggingEmailSender : IEmailSender
{
    private readonly ILogger<LoggingEmailSender> _log;

    public LoggingEmailSender(ILogger<LoggingEmailSender> log) => _log = log;

    public Task SendAsync(string toAddress, string toName, string subject, string textBody,
        CancellationToken ct = default)
    {
        _log.LogInformation("EMAIL (not sent — Log provider)\nTo: {Name} <{Address}>\nSubject: {Subject}\n\n{Body}",
            toName, toAddress, subject, textBody);
        return Task.CompletedTask;
    }
}

/// <summary>Mailtrap (or any SMTP relay) — the safer default while developing.</summary>
public class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _options;
    private readonly ILogger<SmtpEmailSender> _log;

    public SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> log)
    {
        _options = options.Value;
        _log = log;
    }

    public async Task SendAsync(string toAddress, string toName, string subject, string textBody,
        CancellationToken ct = default)
    {
        using var client = new SmtpClient(_options.SmtpHost, _options.SmtpPort)
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(_options.SmtpUser, _options.SmtpPassword)
        };

        using var message = new MailMessage
        {
            From = new MailAddress(_options.FromAddress, _options.FromName),
            Subject = subject,
            Body = textBody,
            IsBodyHtml = false
        };
        message.To.Add(new MailAddress(toAddress, toName));

        await client.SendMailAsync(message, ct);
        _log.LogInformation("Sent email \"{Subject}\" to {Address} via SMTP", subject, toAddress);
    }
}

public class SendGridEmailSender : IEmailSender
{
    private readonly EmailOptions _options;
    private readonly ILogger<SendGridEmailSender> _log;

    public SendGridEmailSender(IOptions<EmailOptions> options, ILogger<SendGridEmailSender> log)
    {
        _options = options.Value;
        _log = log;
    }

    public async Task SendAsync(string toAddress, string toName, string subject, string textBody,
        CancellationToken ct = default)
    {
        var client = new SendGridClient(_options.SendGridApiKey);
        var message = MailHelper.CreateSingleEmail(
            new EmailAddress(_options.FromAddress, _options.FromName),
            new EmailAddress(toAddress, toName),
            subject,
            textBody,
            htmlContent: null);

        var response = await client.SendEmailAsync(message, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Body.ReadAsStringAsync(ct);
            // Throwing lets Hangfire retry the send rather than silently dropping it.
            throw new InvalidOperationException($"SendGrid rejected the message ({response.StatusCode}): {body}");
        }

        _log.LogInformation("Sent email \"{Subject}\" to {Address} via SendGrid", subject, toAddress);
    }
}
