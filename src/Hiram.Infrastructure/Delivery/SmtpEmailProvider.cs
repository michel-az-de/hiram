using Hiram.Application.Delivery;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Hiram.Infrastructure.Delivery;

public sealed class SmtpEmailProvider : IEmailProvider
{
    public string Name => "smtp";

    public async Task<SendOutcome> SendAsync(EmailMessage message, EmailProviderSettings settings, CancellationToken cancellationToken)
    {
        var host = settings.Values.GetValueOrDefault("host");
        var from = settings.Values.GetValueOrDefault("from");
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from)
            || !int.TryParse(settings.Values.GetValueOrDefault("port"), out var port))
        {
            return new SendOutcome.PermanentFailure("SMTP provider requires host, port and from.");
        }

        MimeMessage mime;
        try
        {
            mime = BuildMessage(from, message);
        }
        catch (ParseException ex)
        {
            return new SendOutcome.PermanentFailure($"Invalid email address: {ex.Message}");
        }

        var security = ParseSecurity(settings.Values.GetValueOrDefault("security"));
        var username = settings.Values.GetValueOrDefault("username");

        using var client = new SmtpClient();
        try
        {
            await client.ConnectAsync(host, port, security, cancellationToken);
            if (!string.IsNullOrEmpty(username))
            {
                var secret = settings.Secret;
                if (secret is null)
                    return new SendOutcome.PermanentFailure("SMTP username requires a secret.");

                await client.AuthenticateAsync(username, secret, cancellationToken);
            }
            await client.SendAsync(mime, cancellationToken);
            await client.DisconnectAsync(quit: true, cancellationToken);
            return new SendOutcome.Sent();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (AuthenticationException ex)
        {
            return new SendOutcome.PermanentFailure($"SMTP authentication failed: {ex.Message}");
        }
        catch (SmtpCommandException ex)
        {
            return Classify(ex);
        }
        catch (Exception ex)
        {
            // Connection refused, timeouts and protocol hiccups are worth another attempt.
            return new SendOutcome.TransientFailure($"SMTP send failed: {ex.Message}");
        }
    }

    private static SendOutcome Classify(SmtpCommandException ex)
    {
        if (ex.ErrorCode is SmtpErrorCode.RecipientNotAccepted or SmtpErrorCode.SenderNotAccepted)
            return new SendOutcome.PermanentFailure($"SMTP rejected the address: {(int)ex.StatusCode} {ex.Message}");

        var status = (int)ex.StatusCode;
        return status is >= 400 and < 500
            ? new SendOutcome.TransientFailure($"SMTP temporary error: {status} {ex.Message}")
            : new SendOutcome.PermanentFailure($"SMTP permanent error: {status} {ex.Message}");
    }

    private static MimeMessage BuildMessage(string from, EmailMessage message)
    {
        var mime = new MimeMessage();
        mime.From.Add(MailboxAddress.Parse(from));
        mime.To.Add(MailboxAddress.Parse(message.Recipient));
        mime.Subject = message.Subject;
        mime.Body = new TextPart("plain") { Text = message.Body };
        return mime;
    }

    private static SecureSocketOptions ParseSecurity(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "none" => SecureSocketOptions.None,
        "starttls" => SecureSocketOptions.StartTls,
        "ssl" => SecureSocketOptions.SslOnConnect,
        _ => SecureSocketOptions.Auto
    };
}
