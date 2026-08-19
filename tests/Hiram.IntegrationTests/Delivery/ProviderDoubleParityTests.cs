using System.Net;
using System.Text;
using Hiram.Application.Delivery;
using Hiram.Infrastructure.Delivery;
using Hiram.Simulator.Twilio;

namespace Hiram.IntegrationTests.Delivery;

// The double is only worth running if what it answers is what the real adapters classify. These tests feed
// the double's own bodies to the production adapters, so a drift between the two shows up here instead of
// showing up as a walkthrough that reports the wrong verdict.
public class ProviderDoubleParityTests
{
    private sealed class CannedHandler(ProviderResponse canned) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage((HttpStatusCode)canned.StatusCode)
            {
                Content = new StringContent(canned.Body, Encoding.UTF8, ProviderResponse.ContentType)
            });
    }

    private static SmsProviderSettings SmsSettings() =>
        new(
            new Dictionary<string, string>
            {
                ["account_sid"] = "AC123",
                ["from"] = "+15005550006",
                ["api_key_sid"] = "SK123"
            },
            "shh",
            ProviderConfigOrigin.Tenant);

    private static EmailProviderSettings EmailSettings() =>
        new(
            new Dictionary<string, string>
            {
                ["from"] = "contato@example.test",
                ["api_key_sid"] = "SK123"
            },
            "shh",
            ProviderConfigOrigin.Tenant);

    private static async Task<SendOutcome> SendSmsAsync(ProviderResponse canned)
    {
        var http = new HttpClient(new CannedHandler(canned)) { BaseAddress = new Uri("https://api.twilio.com/") };
        return await new TwilioSmsProvider(http).SendAsync(
            new SmsMessage("+5511999990000", "Seu pedido 42 saiu para entrega."), SmsSettings(), CancellationToken.None);
    }

    private static async Task<SendOutcome> SendEmailAsync(ProviderResponse canned)
    {
        var http = new HttpClient(new CannedHandler(canned)) { BaseAddress = new Uri("https://comms.twilio.com/v1/") };
        return await new TwilioEmailProvider(http).SendAsync(
            new EmailMessage("alguem@example.test", "Pedido enviado", "<p>Pedido enviado</p>"), EmailSettings(), CancellationToken.None);
    }

    [Fact]
    public async Task AcceptedMessage_ClassifiesAsSent_WithTheDoublesSid()
    {
        var outcome = await SendSmsAsync(MessagesResource.For(ProviderScenario.Accept, "SM00000000000000000000000000000001"));

        var sent = Assert.IsType<SendOutcome.Sent>(outcome);
        Assert.Equal("SM00000000000000000000000000000001", sent.ProviderMessageId);
    }

    [Theory]
    [InlineData(ProviderScenario.GeoPermissionDenied, "21408")]
    [InlineData(ProviderScenario.RecipientOptedOut, "21610")]
    [InlineData(ProviderScenario.OutsideSessionWindow, "63016")]
    [InlineData(ProviderScenario.TemplateRequired, "21654")]
    [InlineData(ProviderScenario.CampaignNotRegistered, "30034")]
    public async Task RefusedMessage_ClassifiesAsPermanent_AndNamesTheProviderCode(ProviderScenario scenario, string code)
    {
        var outcome = await SendSmsAsync(MessagesResource.For(scenario, "SM1"));

        var failure = Assert.IsType<SendOutcome.PermanentFailure>(outcome);
        Assert.Contains(code, failure.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MessageAcceptedThenFilteredByTheCarrier_ClassifiesAsPermanent()
    {
        // Twilio answers 201 and carries the verdict on the message itself. The double has to produce that,
        // because it is the only way the carrier codes ever arrive.
        var outcome = await SendSmsAsync(MessagesResource.For(ProviderScenario.CarrierFiltered, "SM1"));

        var failure = Assert.IsType<SendOutcome.PermanentFailure>(outcome);
        Assert.Contains("30007", failure.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MessageAcceptedThenReportedUnreachable_ClassifiesAsTransient()
    {
        var outcome = await SendSmsAsync(MessagesResource.For(ProviderScenario.UnreachableHandset, "SM1"));

        Assert.IsType<SendOutcome.TransientFailure>(outcome);
    }

    [Fact]
    public async Task MessageAcceptedThenReportedUnknownNumber_NamesTheDestinationAsInvalid()
    {
        var outcome = await SendSmsAsync(MessagesResource.For(ProviderScenario.UnknownHandset, "SM1"));

        var failure = Assert.IsType<SendOutcome.PermanentFailure>(outcome);
        Assert.Equal(DeliveryFailureKind.InvalidDestination, failure.Kind);
    }

    [Theory]
    [InlineData(ProviderScenario.RateLimited)]
    [InlineData(ProviderScenario.ServerError)]
    public async Task RetryableMessage_ClassifiesAsTransient(ProviderScenario scenario)
    {
        var outcome = await SendSmsAsync(MessagesResource.For(scenario, "SM1"));

        Assert.IsType<SendOutcome.TransientFailure>(outcome);
    }

    [Fact]
    public async Task MissingCredential_ClassifiesAsPermanent()
    {
        var outcome = await SendSmsAsync(MessagesResource.Unauthorized());

        var failure = Assert.IsType<SendOutcome.PermanentFailure>(outcome);
        Assert.Contains("20003", failure.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AcceptedEmail_ClassifiesAsSent_WithTheDoublesOperationId()
    {
        var outcome = await SendEmailAsync(EmailsResource.For(ProviderScenario.Accept, "EM00000000000000000000000000000001"));

        var sent = Assert.IsType<SendOutcome.Sent>(outcome);
        Assert.Equal("EM00000000000000000000000000000001", sent.ProviderMessageId);
    }

    [Fact]
    public async Task RefusedEmail_ClassifiesAsPermanent_AndCarriesTheProvidersText()
    {
        var outcome = await SendEmailAsync(EmailsResource.For(ProviderScenario.GeoPermissionDenied, "EM1"));

        var failure = Assert.IsType<SendOutcome.PermanentFailure>(outcome);
        Assert.Contains("rejected the request", failure.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ProviderScenario.RateLimited)]
    [InlineData(ProviderScenario.ServerError)]
    public async Task RetryableEmail_ClassifiesAsTransient(ProviderScenario scenario)
    {
        var outcome = await SendEmailAsync(EmailsResource.For(scenario, "EM1"));

        Assert.IsType<SendOutcome.TransientFailure>(outcome);
    }
}
