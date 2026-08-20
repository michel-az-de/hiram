using System.Net;
using System.Text;
using Hiram.Application.Delivery;
using Hiram.Infrastructure.Delivery;

namespace Hiram.IntegrationTests.Delivery;

public class TwilioWhatsAppProviderTests
{
    private sealed class CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? LastBody { get; private set; }
        public string? LastUri { get; private set; }
        public string? LastAuthorization { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastUri = request.RequestUri?.ToString();
            LastAuthorization = request.Headers.Authorization?.ToString();
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return responder(request);
        }
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw exception;
    }

    private static HttpResponseMessage Queued(string sid) =>
        new(HttpStatusCode.Created)
        {
            Content = new StringContent($$"""{"sid":"{{sid}}","status":"queued","error_code":null}""")
        };

    private static HttpResponseMessage Refused(HttpStatusCode status, int? code = null, string? message = null) =>
        new(status)
        {
            Content = new StringContent(
                code is null ? "{}" : $$"""{"code":{{code}},"message":"{{message}}","status":{{(int)status}}}""")
        };

    private static WhatsAppProviderSettings Settings(
        string? accountSid = "AC123",
        string? from = "+14155238886",
        string? apiKeySid = "SK123",
        string? secret = "shh")
    {
        var values = new Dictionary<string, string>();
        if (accountSid is not null) values["account_sid"] = accountSid;
        if (from is not null) values["from"] = from;
        if (apiKeySid is not null) values["api_key_sid"] = apiKeySid;

        return new WhatsAppProviderSettings(values, secret, ProviderConfigOrigin.Tenant);
    }

    private static WhatsAppMessage Message() =>
        new WhatsAppMessage.FreeForm("+5511982254398", "Seu pedido 42 saiu para entrega.");

    private static WhatsAppMessage TemplateMessage() =>
        new WhatsAppMessage.Template("+5511982254398", "order_shipped", "pt_BR", ["42"]);

    private static TwilioWhatsAppProvider Provider(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.twilio.com/") };
        return new TwilioWhatsAppProvider(http);
    }

    [Fact]
    public async Task Send_ReturnsSentWithMessageSid_WhenQueued()
    {
        var handler = new CapturingHandler(_ => Queued("SM19068771"));

        var outcome = await Provider(handler).SendAsync(Message(), Settings(), CancellationToken.None);

        var sent = Assert.IsType<SendOutcome.Sent>(outcome);
        Assert.Equal("SM19068771", sent.ProviderMessageId);
    }

    [Fact]
    public async Task Send_PostsToTheAccountMessagesResource()
    {
        var handler = new CapturingHandler(_ => Queued("SM1"));

        await Provider(handler).SendAsync(Message(), Settings(accountSid: "AC999"), CancellationToken.None);

        Assert.Equal("https://api.twilio.com/2010-04-01/Accounts/AC999/Messages.json", handler.LastUri);
        var expectedAuth = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("SK123:shh"));
        Assert.Equal(expectedAuth, handler.LastAuthorization);
    }

    [Fact]
    public async Task Send_PrefixesBothAddresses_WithTheWhatsAppScheme()
    {
        var handler = new CapturingHandler(_ => Queued("SM1"));

        await Provider(handler).SendAsync(Message(), Settings(), CancellationToken.None);

        // The same Messages resource serves both channels and the prefix is what picks WhatsApp, so it
        // has to be here and only here: the stored recipient stays a bare E.164 number.
        Assert.Contains("To=whatsapp%3A%2B5511982254398", handler.LastBody);
        Assert.Contains("From=whatsapp%3A%2B14155238886", handler.LastBody);
    }

    [Fact]
    public async Task Send_PostsTheNotificationBody_Unaltered()
    {
        var handler = new CapturingHandler(_ => Queued("SM1"));

        await Provider(handler).SendAsync(Message(), Settings(), CancellationToken.None);

        // Free text is accepted inside the session window, so unlike SMS and email there is no trial
        // substitution here and the persisted body is what leaves.
        Assert.Contains("Seu+pedido+42+saiu+para+entrega.", handler.LastBody);
    }

    [Fact]
    public async Task Send_FailsPermanently_WhenConfigurationIsIncomplete()
    {
        var handler = new CapturingHandler(_ => Queued("SM1"));

        var outcome = await Provider(handler).SendAsync(Message(), Settings(from: null), CancellationToken.None);

        Assert.IsType<SendOutcome.PermanentFailure>(outcome);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Send_CarriesTheProviderReason_WhenTheSessionWindowIsClosed()
    {
        // 63016 is the failure this channel hits first: free text sent to someone whose 24h window has
        // expired. Permanent, so it dead letters named instead of burning retries, and a replay after the
        // recipient rejoins the sandbox can still deliver it.
        var handler = new CapturingHandler(_ => Refused(
            HttpStatusCode.BadRequest, 63016,
            "Failed to send freeform message because you are outside the allowed window."));

        var outcome = await Provider(handler).SendAsync(Message(), Settings(), CancellationToken.None);

        var failure = Assert.IsType<SendOutcome.PermanentFailure>(outcome);
        Assert.Contains("63016", failure.Reason);
        Assert.Contains("outside the allowed window", failure.Reason);
    }

    [Fact]
    public async Task Send_CarriesTheProviderReason_WhenRecipientIsUnverified()
    {
        var handler = new CapturingHandler(_ => Refused(
            HttpStatusCode.BadRequest, 21608, "The number is unverified. Trial accounts may only send to verified numbers."));

        var outcome = await Provider(handler).SendAsync(Message(), Settings(), CancellationToken.None);

        var failure = Assert.IsType<SendOutcome.PermanentFailure>(outcome);
        Assert.Contains("21608", failure.Reason);
        Assert.Contains("unverified", failure.Reason);
    }

    [Fact]
    public async Task Send_FailsPermanently_WhenTheMessageIsAcceptedButAlreadyFailed()
    {
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("""{"sid":"SM1","status":"failed","error_message":"blocked"}""")
        });

        var outcome = await Provider(handler).SendAsync(Message(), Settings(), CancellationToken.None);

        // A 2xx that already reports a terminal status is not a send, and retrying it would only burn credit.
        Assert.IsType<SendOutcome.PermanentFailure>(outcome);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task Send_IsTransient_WhenThrottledOrServerSide(HttpStatusCode status)
    {
        var handler = new CapturingHandler(_ => Refused(status));

        var outcome = await Provider(handler).SendAsync(Message(), Settings(), CancellationToken.None);

        Assert.IsType<SendOutcome.TransientFailure>(outcome);
    }

    [Fact]
    public async Task Send_IsTransient_WhenTransportFails()
    {
        var outcome = await Provider(new ThrowingHandler(new HttpRequestException("reset")))
            .SendAsync(Message(), Settings(), CancellationToken.None);

        Assert.IsType<SendOutcome.TransientFailure>(outcome);
    }

    [Fact]
    public async Task Send_RefusesATemplate_BecauseThisAdapterOnlySendsFreeForm()
    {
        var handler = new CapturingHandler(_ => Queued("SM1"));

        var outcome = await Provider(handler).SendAsync(TemplateMessage(), Settings(), CancellationToken.None);

        // Configuration, not Provider: the tenant asked for a shape this adapter cannot send, and the fix is
        // to point the tenant at the Meta adapter, not to retry or to blame the recipient.
        var failure = Assert.IsType<SendOutcome.PermanentFailure>(outcome);
        Assert.Equal(DeliveryFailureKind.Configuration, failure.Kind);
        Assert.Contains("template", failure.Reason, StringComparison.OrdinalIgnoreCase);

        // Nothing left for the provider. Flattening the template into text would have delivered either raw
        // placeholders or wording nobody approved, and a request that never happened cannot do either.
        Assert.Equal(0, handler.Calls);
    }
}
