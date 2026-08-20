using System.Net;
using System.Text;
using Hiram.Application.Delivery;
using Hiram.Infrastructure.Delivery;
using Hiram.Simulator.Meta;
using Hiram.Simulator.Providers;

namespace Hiram.IntegrationTests.Delivery;

// The Meta double is only worth running if what it answers is what the real adapter classifies. These
// tests feed the double's own bodies to MetaWhatsAppProvider, so a drift between the two shows up here
// instead of showing up as a walkthrough reporting the wrong verdict. Same contract as
// ProviderDoubleParityTests, for the second provider.
public class MetaDoubleParityTests
{
    private sealed class CannedHandler(ProviderResponse canned) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage((HttpStatusCode)canned.StatusCode)
            {
                Content = new StringContent(canned.Body, Encoding.UTF8, ProviderResponse.ContentType)
            });
    }

    private static WhatsAppProviderSettings Settings() =>
        new(new Dictionary<string, string> { ["phone_number_id"] = "100000000000000" }, "EAA-token", ProviderConfigOrigin.Tenant);

    private static async Task<SendOutcome> SendAsync(ProviderResponse canned)
    {
        var http = new HttpClient(new CannedHandler(canned)) { BaseAddress = new Uri("https://graph.facebook.com/") };
        return await new MetaWhatsAppProvider(http, "v23.0").SendAsync(
            new WhatsAppMessage.FreeForm("+5511999990000", "Seu pedido 42 saiu para entrega."),
            Settings(),
            CancellationToken.None);
    }

    private static ProviderResponse Answer(ProviderScenario scenario, string wamid = "wamid.parity") =>
        MetaMessagesResource.For(scenario, wamid)
            ?? throw new InvalidOperationException($"The Meta double has no answer for '{scenario}'.");

    [Fact]
    public async Task AcceptedMessage_ClassifiesAsSent_WithTheDoublesWamid()
    {
        var outcome = await SendAsync(Answer(ProviderScenario.Accept, "wamid.HBgNSIMULATOR000001"));

        var sent = Assert.IsType<SendOutcome.Sent>(outcome);
        Assert.Equal("wamid.HBgNSIMULATOR000001", sent.ProviderMessageId);
    }

    [Theory]
    [InlineData(ProviderScenario.OutsideSessionWindow, "131047")]
    [InlineData(ProviderScenario.TemplateRequired, "132001")]
    [InlineData(ProviderScenario.TemplateParametersMismatch, "132000")]
    [InlineData(ProviderScenario.TokenExpired, "190")]
    [InlineData(ProviderScenario.AccountRestricted, "131031")]
    public async Task RefusedMessage_ClassifiesAsConfiguration_AndNamesTheMetaCode(ProviderScenario scenario, string code)
    {
        var outcome = await SendAsync(Answer(scenario));

        // Configuration, not Provider: every one of these needs a person in a console, and the dead letter
        // has to say which code so nobody goes looking at the recipient.
        var failure = Assert.IsType<SendOutcome.PermanentFailure>(outcome);
        Assert.Equal(DeliveryFailureKind.Configuration, failure.Kind);
        Assert.Contains(code, failure.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecipientNotOnWhatsApp_NamesTheDestinationAsInvalid()
    {
        var outcome = await SendAsync(Answer(ProviderScenario.UnknownHandset));

        var failure = Assert.IsType<SendOutcome.PermanentFailure>(outcome);
        Assert.Equal(DeliveryFailureKind.InvalidDestination, failure.Kind);
    }

    [Theory]
    [InlineData(ProviderScenario.RateLimited)]
    [InlineData(ProviderScenario.ServerError)]
    public async Task RetryableMessage_ClassifiesAsTransient(ProviderScenario scenario)
    {
        var outcome = await SendAsync(Answer(scenario));

        Assert.IsType<SendOutcome.TransientFailure>(outcome);
    }

    [Fact]
    public async Task MissingCredential_ClassifiesAsConfiguration()
    {
        // What the double answers an adapter that forgot to authenticate. It has to land on the operator's
        // side, not read as a recipient problem.
        var outcome = await SendAsync(MetaMessagesResource.Unauthorized());

        var failure = Assert.IsType<SendOutcome.PermanentFailure>(outcome);
        Assert.Equal(DeliveryFailureKind.Configuration, failure.Kind);
    }

    [Fact]
    public void EveryScenarioTheDoubleAnswers_IsOneTheAdapterClassifies()
    {
        var meta = new MetaDouble(ProviderScenario.Accept);

        // Derived from the enum rather than listed by hand (ADR-029): a scenario added later shows up here
        // as a failure instead of quietly going untested.
        var answered = Enum.GetValues<ProviderScenario>().Where(meta.Supports).ToArray();

        Assert.Contains(ProviderScenario.Accept, answered);
        Assert.DoesNotContain(ProviderScenario.CampaignNotRegistered, answered);
        Assert.DoesNotContain(ProviderScenario.CarrierFiltered, answered);
        Assert.All(answered, scenario => Assert.NotNull(MetaMessagesResource.For(scenario, "wamid.1")));
    }
}
