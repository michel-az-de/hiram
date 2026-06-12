using System.Net.Http.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Hiram.Application.Delivery;
using Hiram.Infrastructure.Delivery;

namespace Hiram.IntegrationTests.Delivery;

public class SmtpDeliveryTests : IAsyncLifetime
{
    private readonly IContainer _mailpit = new ContainerBuilder("axllent/mailpit:latest")
        .WithPortBinding(1025, true)
        .WithPortBinding(8025, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(8025))
        .Build();

    public Task InitializeAsync() => _mailpit.StartAsync();

    public Task DisposeAsync() => _mailpit.DisposeAsync().AsTask();

    [Fact]
    public async Task Send_DeliversMessageToMailpit()
    {
        var settings = new EmailProviderSettings(
            new Dictionary<string, string>
            {
                ["host"] = _mailpit.Hostname,
                ["port"] = _mailpit.GetMappedPublicPort(1025).ToString(),
                ["from"] = "no-reply@hiram.dev",
                ["security"] = "none"
            },
            Secret: null);

        var provider = new SmtpEmailProvider();
        var outcome = await provider.SendAsync(
            new EmailMessage("ops@example.com", "hello from hiram", "f1 body"), settings, CancellationToken.None);

        Assert.IsType<SendOutcome.Sent>(outcome);

        using var http = new HttpClient { BaseAddress = new Uri($"http://{_mailpit.Hostname}:{_mailpit.GetMappedPublicPort(8025)}") };

        MailpitMessages? inbox = null;
        for (var attempt = 0; attempt < 20 && (inbox?.Messages.Count ?? 0) == 0; attempt++)
        {
            inbox = await http.GetFromJsonAsync<MailpitMessages>("/api/v1/messages");
            if ((inbox?.Messages.Count ?? 0) == 0)
                await Task.Delay(200);
        }

        Assert.NotNull(inbox);
        Assert.Contains(inbox!.Messages, m =>
            m.Subject == "hello from hiram" && m.To.Any(t => t.Address == "ops@example.com"));
    }

    private sealed record MailpitMessages(List<MailpitMessage> Messages);

    private sealed record MailpitMessage(string Subject, List<MailpitAddress> To);

    private sealed record MailpitAddress(string Address);
}
