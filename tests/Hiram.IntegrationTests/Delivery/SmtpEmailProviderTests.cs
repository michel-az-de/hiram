using System.Diagnostics.Metrics;
using Hiram.Application.Delivery;
using Hiram.Infrastructure.Delivery;

namespace Hiram.IntegrationTests.Delivery;

public class SmtpEmailProviderTests
{
    private static EmailProviderSettings Settings(
        ProviderConfigOrigin origin, params (string Key, string Value)[] values) =>
        new(values.ToDictionary(v => v.Key, v => v.Value), Secret: null, origin);

    private static SmtpEmailProvider Provider() => new(new SmtpDestinationPolicy());

    [Fact]
    public async Task Send_ReturnsPermanent_WhenMisconfigured()
    {
        var outcome = await Provider().SendAsync(
            new EmailMessage("ops@example.com", "hello", "body"),
            Settings(ProviderConfigOrigin.Platform, ("from", "no-reply@hiram.dev")),
            CancellationToken.None);

        Assert.IsType<SendOutcome.PermanentFailure>(outcome);
    }

    [Fact]
    public async Task Send_ReturnsTransient_WhenServerUnreachable()
    {
        // Platform origin skips the tenant guard, so a loopback target still exercises the connect path.
        var outcome = await Provider().SendAsync(
            new EmailMessage("ops@example.com", "hello", "body"),
            Settings(ProviderConfigOrigin.Platform,
                ("host", "127.0.0.1"), ("port", "1"), ("from", "no-reply@hiram.dev"), ("security", "none")),
            CancellationToken.None);

        Assert.IsType<SendOutcome.TransientFailure>(outcome);
    }

    [Fact]
    public async Task Send_ReturnsPermanent_WhenTenantSmtpHasNoTls()
    {
        var outcome = await Provider().SendAsync(
            new EmailMessage("ops@example.com", "hello", "body"),
            Settings(ProviderConfigOrigin.Tenant,
                ("host", "smtp.example.com"), ("port", "587"), ("from", "no-reply@hiram.dev"), ("security", "none")),
            CancellationToken.None);

        Assert.IsType<SendOutcome.PermanentFailure>(outcome);
    }

    [Theory]
    [InlineData("169.254.169.254")]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    public async Task Send_ReturnsPermanent_WhenTenantTargetsInternalHost(string host)
    {
        var outcome = await Provider().SendAsync(
            new EmailMessage("ops@example.com", "hello", "body"),
            Settings(ProviderConfigOrigin.Tenant,
                ("host", host), ("port", "587"), ("from", "no-reply@hiram.dev"), ("security", "starttls")),
            CancellationToken.None);

        Assert.IsType<SendOutcome.PermanentFailure>(outcome);
    }

    [Fact]
    public async Task Send_IncrementsRejectionCounter_WhenTenantTargetsInternalHost()
    {
        var rejections = 0L;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == "hiram.smtp.destination_rejected")
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => Interlocked.Add(ref rejections, value));
        listener.Start();

        await Provider().SendAsync(
            new EmailMessage("ops@example.com", "hello", "body"),
            Settings(ProviderConfigOrigin.Tenant,
                ("host", "169.254.169.254"), ("port", "587"), ("from", "no-reply@hiram.dev"), ("security", "starttls")),
            CancellationToken.None);

        Assert.True(rejections >= 1);
    }
}
