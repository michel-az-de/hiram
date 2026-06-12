using Hiram.Application.Delivery;
using Hiram.Infrastructure.Delivery;

namespace Hiram.IntegrationTests.Delivery;

public class SmtpEmailProviderTests
{
    private static EmailProviderSettings Settings(params (string Key, string Value)[] values) =>
        new(values.ToDictionary(v => v.Key, v => v.Value), Secret: null);

    [Fact]
    public async Task Send_ReturnsPermanent_WhenMisconfigured()
    {
        var provider = new SmtpEmailProvider();

        var outcome = await provider.SendAsync(
            new EmailMessage("ops@example.com", "hello", "body"),
            Settings(("from", "no-reply@hiram.dev")),
            CancellationToken.None);

        Assert.IsType<SendOutcome.PermanentFailure>(outcome);
    }

    [Fact]
    public async Task Send_ReturnsTransient_WhenServerUnreachable()
    {
        var provider = new SmtpEmailProvider();

        var outcome = await provider.SendAsync(
            new EmailMessage("ops@example.com", "hello", "body"),
            Settings(("host", "127.0.0.1"), ("port", "1"), ("from", "no-reply@hiram.dev"), ("security", "none")),
            CancellationToken.None);

        Assert.IsType<SendOutcome.TransientFailure>(outcome);
    }
}
