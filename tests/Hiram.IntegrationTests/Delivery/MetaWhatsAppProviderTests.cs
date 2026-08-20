using System.Net;
using System.Text.Json;
using Hiram.Application.Delivery;
using Hiram.Infrastructure.Delivery;

namespace Hiram.IntegrationTests.Delivery;

public class MetaWhatsAppProviderTests
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

    private static HttpResponseMessage Accepted(string wamid) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""{"messaging_product":"whatsapp","contacts":[{"wa_id":"5511982254398"}],"messages":[{"id":"{{wamid}}"}]}""")
        };

    private static HttpResponseMessage Refused(HttpStatusCode status, int? code = null, string? details = null) =>
        new(status)
        {
            Content = new StringContent(
                code is null
                    ? "{}"
                    : $$$"""{"error":{"message":"Error","type":"OAuthException","code":{{{code}}},"error_data":{"messaging_product":"whatsapp","details":"{{{details}}}"},"fbtrace_id":"A1"}}""")
        };

    private static WhatsAppProviderSettings Settings(
        string? phoneNumberId = "109876543210",
        string? secret = "EAA-system-user-token",
        string? graphVersion = null)
    {
        var values = new Dictionary<string, string>();
        if (phoneNumberId is not null) values["phone_number_id"] = phoneNumberId;
        if (graphVersion is not null) values["graph_version"] = graphVersion;

        return new WhatsAppProviderSettings(values, secret, ProviderConfigOrigin.Tenant);
    }

    private static WhatsAppMessage FreeForm() =>
        new WhatsAppMessage.FreeForm("+5511982254398", "Seu pedido 42 saiu para entrega.");

    private static WhatsAppMessage Template() =>
        new WhatsAppMessage.Template("+5511982254398", "order_shipped", "pt_BR", ["42", "hoje"]);

    private static MetaWhatsAppProvider Provider(HttpMessageHandler handler, string defaultVersion = "v23.0")
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://graph.facebook.com/") };
        return new MetaWhatsAppProvider(http, defaultVersion);
    }

    [Fact]
    public async Task Send_ReturnsSentWithTheWamid_WhenAccepted()
    {
        var handler = new CapturingHandler(_ => Accepted("wamid.HBgNNTUxMTk4MjI1NDM5OBUCABEYEjcy"));

        var outcome = await Provider(handler).SendAsync(FreeForm(), Settings(), CancellationToken.None);

        // messages[0].id is what a status callback matches on later, so losing it here would make the whole
        // delivered and read loop impossible.
        var sent = Assert.IsType<SendOutcome.Sent>(outcome);
        Assert.Equal("wamid.HBgNNTUxMTk4MjI1NDM5OBUCABEYEjcy", sent.ProviderMessageId);
        Assert.False(sent.TrialContent);
    }

    [Fact]
    public async Task Send_PostsToTheVersionedPhoneNumberResource_WithABearerToken()
    {
        var handler = new CapturingHandler(_ => Accepted("wamid.1"));

        await Provider(handler).SendAsync(FreeForm(), Settings(), CancellationToken.None);

        Assert.Equal("https://graph.facebook.com/v23.0/109876543210/messages", handler.LastUri);
        Assert.Equal("Bearer EAA-system-user-token", handler.LastAuthorization);
    }

    [Fact]
    public async Task Send_UsesTheTenantGraphVersion_WhenItPinsOne()
    {
        var handler = new CapturingHandler(_ => Accepted("wamid.1"));

        await Provider(handler).SendAsync(FreeForm(), Settings(graphVersion: "v26.0"), CancellationToken.None);

        // A tenant mid-migration can move ahead of the host default without a deploy.
        Assert.Equal("https://graph.facebook.com/v26.0/109876543210/messages", handler.LastUri);
    }

    [Fact]
    public async Task Send_WritesTheFreeFormShape_TheCloudApiExpects()
    {
        var handler = new CapturingHandler(_ => Accepted("wamid.1"));

        await Provider(handler).SendAsync(FreeForm(), Settings(), CancellationToken.None);

        using var payload = JsonDocument.Parse(handler.LastBody!);
        var root = payload.RootElement;

        Assert.Equal("whatsapp", root.GetProperty("messaging_product").GetString());
        Assert.Equal("individual", root.GetProperty("recipient_type").GetString());
        Assert.Equal("+5511982254398", root.GetProperty("to").GetString());
        Assert.Equal("text", root.GetProperty("type").GetString());
        Assert.Equal("Seu pedido 42 saiu para entrega.", root.GetProperty("text").GetProperty("body").GetString());

        // No template key at all on a free form send: Meta rejects a payload that carries both.
        Assert.False(root.TryGetProperty("template", out _));
    }

    [Fact]
    public async Task Send_WritesTheTemplateShape_WithParametersInOrder()
    {
        var handler = new CapturingHandler(_ => Accepted("wamid.1"));

        await Provider(handler).SendAsync(Template(), Settings(), CancellationToken.None);

        using var payload = JsonDocument.Parse(handler.LastBody!);
        var root = payload.RootElement;

        Assert.Equal("template", root.GetProperty("type").GetString());
        Assert.False(root.TryGetProperty("text", out _));

        var template = root.GetProperty("template");
        Assert.Equal("order_shipped", template.GetProperty("name").GetString());
        Assert.Equal("pt_BR", template.GetProperty("language").GetProperty("code").GetString());

        // Position is the whole contract of an HSM: the values fill numbered placeholders, so reordering
        // them silently sends a different message rather than failing.
        var parameters = template.GetProperty("components")[0].GetProperty("parameters");
        Assert.Equal(2, parameters.GetArrayLength());
        Assert.Equal("42", parameters[0].GetProperty("text").GetString());
        Assert.Equal("hoje", parameters[1].GetProperty("text").GetString());
    }

    [Theory]
    [InlineData(null, "EAA-token")]
    [InlineData("109876543210", null)]
    public async Task Send_IsAConfigurationFailure_WhenTheTenantConfigIsIncomplete(string? phoneNumberId, string? secret)
    {
        var handler = new CapturingHandler(_ => Accepted("wamid.1"));

        var outcome = await Provider(handler).SendAsync(
            FreeForm(), Settings(phoneNumberId, secret), CancellationToken.None);

        var failure = Assert.IsType<SendOutcome.PermanentFailure>(outcome);
        Assert.Equal(DeliveryFailureKind.Configuration, failure.Kind);

        // Nothing leaves. A request built from a missing token would come back as a 401 that reads like a
        // revoked credential, sending whoever is on call to the wrong console.
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    // Every code Meta itself resolves: the throughput limits, the quality pause, the per-recipient burst,
    // and its own unknown failure. All arrive as 4xx or 5xx and all deserve another attempt.
    [InlineData(4, HttpStatusCode.TooManyRequests)]
    [InlineData(80007, HttpStatusCode.TooManyRequests)]
    [InlineData(130429, HttpStatusCode.TooManyRequests)]
    [InlineData(131048, HttpStatusCode.TooManyRequests)]
    [InlineData(131056, HttpStatusCode.TooManyRequests)]
    [InlineData(131000, HttpStatusCode.InternalServerError)]
    public async Task Send_IsTransient_WhenMetaResolvesItOnItsOwn(int code, HttpStatusCode status)
    {
        var handler = new CapturingHandler(_ => Refused(status, code, "try later"));

        var outcome = await Provider(handler).SendAsync(FreeForm(), Settings(), CancellationToken.None);

        var transient = Assert.IsType<SendOutcome.TransientFailure>(outcome);
        Assert.Contains(code.ToString(), transient.Reason, StringComparison.Ordinal);
    }

    [Theory]
    // Everything a person has to fix: the closed 24h window, the five template faults, and the five ways a
    // business account can be unable to send. Meta answers 400 for most of them, and a range rule would
    // have retried every one.
    [InlineData(131047)]
    [InlineData(132001)]
    [InlineData(132000)]
    [InlineData(132012)]
    [InlineData(132007)]
    [InlineData(132015)]
    [InlineData(131042)]
    [InlineData(133010)]
    [InlineData(190)]
    [InlineData(368)]
    [InlineData(131031)]
    public async Task Send_IsAConfigurationFailure_WhenSomeoneHasToFixTheAccountOrTheTemplate(int code)
    {
        var handler = new CapturingHandler(_ => Refused(HttpStatusCode.BadRequest, code, "operator action required"));

        var outcome = await Provider(handler).SendAsync(Template(), Settings(), CancellationToken.None);

        var failure = Assert.IsType<SendOutcome.PermanentFailure>(outcome);
        Assert.Equal(DeliveryFailureKind.Configuration, failure.Kind);
        Assert.Contains("operator action required", failure.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Send_BlamesTheDestination_WhenTheRecipientIsNotOnWhatsApp()
    {
        var handler = new CapturingHandler(_ => Refused(HttpStatusCode.BadRequest, 131026, "Receiver is incapable"));

        var outcome = await Provider(handler).SendAsync(FreeForm(), Settings(), CancellationToken.None);

        // The contact is wrong at the source. Another recipient works, and a retry to this one never will.
        var failure = Assert.IsType<SendOutcome.PermanentFailure>(outcome);
        Assert.Equal(DeliveryFailureKind.InvalidDestination, failure.Kind);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task Send_FallsBackToTheStatusRange_WhenTheCodeIsUnmapped(HttpStatusCode status)
    {
        var handler = new CapturingHandler(_ => Refused(status, 999999, "brand new code"));

        var outcome = await Provider(handler).SendAsync(FreeForm(), Settings(), CancellationToken.None);

        // An unmapped code is generic rather than mislabelled, which is what lets Meta add codes without
        // this adapter silently deciding they are permanent.
        Assert.IsType<SendOutcome.TransientFailure>(outcome);
    }

    [Fact]
    public async Task Send_IsPermanent_WhenTheBodyDoesNotParseAndTheStatusIsClientSide()
    {
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("<html>gateway</html>")
        });

        var outcome = await Provider(handler).SendAsync(FreeForm(), Settings(), CancellationToken.None);

        var failure = Assert.IsType<SendOutcome.PermanentFailure>(outcome);
        Assert.Contains("400", failure.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Send_IsTransient_WhenTransportFails()
    {
        var outcome = await Provider(new ThrowingHandler(new HttpRequestException("reset")))
            .SendAsync(FreeForm(), Settings(), CancellationToken.None);

        Assert.IsType<SendOutcome.TransientFailure>(outcome);
    }

    [Fact]
    public async Task Send_PropagatesCancellation_RatherThanReportingItAsAFailure()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        // A cancelled token turns HttpClient's own failure into a TaskCanceledException, which also matches
        // the transport catch below it. A shutdown is not a delivery verdict: swallowing it would record a
        // retryable attempt the provider never even saw.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Provider(new ThrowingHandler(new TaskCanceledException()))
                .SendAsync(FreeForm(), Settings(), cancelled.Token));
    }
}
