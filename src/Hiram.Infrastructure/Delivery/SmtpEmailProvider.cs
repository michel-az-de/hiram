using Hiram.Application.Delivery;
using Hiram.Infrastructure.Telemetry;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Hiram.Infrastructure.Delivery;

public sealed class SmtpEmailProvider : IEmailProvider
{
    private readonly ISmtpDestinationPolicy _destinations;

    public SmtpEmailProvider(ISmtpDestinationPolicy destinations)
    {
        _destinations = destinations;
    }

    public string Name => ProviderNames.Smtp;

    public async Task<SendOutcome> SendAsync(EmailMessage message, EmailProviderSettings settings, CancellationToken cancellationToken)
    {
        var host = settings.Values.GetValueOrDefault("host");
        var from = settings.Values.GetValueOrDefault("from");
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from)
            || !int.TryParse(settings.Values.GetValueOrDefault("port"), out var port))
        {
            return new SendOutcome.PermanentFailure("SMTP provider requires host, port and from.");
        }

        var security = ParseSecurity(settings.Values.GetValueOrDefault("security"));

        if (settings.Origin == ProviderConfigOrigin.Tenant)
        {
            // A tenant may point this host anywhere, so it must be encrypted and must not resolve to our
            // own network. The platform default is the operator's and is trusted to reach an internal MTA.
            if (security is not (SecureSocketOptions.StartTls or SecureSocketOptions.SslOnConnect))
                return new SendOutcome.PermanentFailure("Tenant SMTP requires TLS (starttls or ssl).");

            switch (await _destinations.InspectAsync(host, cancellationToken))
            {
                case SmtpDestinationVerdict.Blocked:
                    HiramDiagnostics.SmtpDestinationRejected.Add(1);
                    return new SendOutcome.PermanentFailure("SMTP destination is not allowed.");
                case SmtpDestinationVerdict.Unresolved:
                    return new SendOutcome.TransientFailure("SMTP host did not resolve.");
            }
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
        catch (SslHandshakeException ex)
        {
            // An untrusted or mismatched TLS certificate will not fix itself on retry: permanent.
            return new SendOutcome.PermanentFailure($"SMTP TLS handshake failed: {ex.Message}");
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
