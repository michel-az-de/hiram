using System.Net;
using System.Text;
using Hiram.Application.Delivery;
using Hiram.Infrastructure.Delivery;

namespace Hiram.IntegrationTests.Delivery;

public class TwilioSmsProviderTests
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

    private static SmsProviderSettings Settings(
        string? accountSid = "AC123",
        string? from = "+17372212163",
        string? apiKeySid = "SK123",
        string? secret = "shh",
        bool trial = false,
        string? trialTemplate = "sms_account_alerts")
    {
        var values = new Dictionary<string, string>();
        if (accountSid is not null) values["account_sid"] = accountSid;
        if (from is not null) values["from"] = from;
        if (apiKeySid is not null) values["api_key_sid"] = apiKeySid;
        if (trial)
        {
            values["trial_mode"] = "true";
            if (trialTemplate is not null) values["trial_template"] = trialTemplate;
        }

        return new SmsProviderSettings(values, secret, ProviderConfigOrigin.Tenant);
    }

    private static SmsMessage Message() => new("+5511982254398", "Seu pedido 42 saiu para entrega.");

    private static TwilioSmsProvider Provider(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.twilio.com/") };
        return new TwilioSmsProvider(http);
    }

    [Fact]
    public async Task Send_ReturnsSentWithMessageSid_WhenQueued()
    {
        var handler = new CapturingHandler(_ => Queued("SM19068771"));

        var outcome = await Provider(handler).SendAsync(Message(), Settings(), CancellationToken.None);

        var sent = Assert.IsType<SendOutcome.Sent>(outcome);
        Assert.Equal("SM19068771", sent.ProviderMessageId);

        // Outside trial the notification's own body went out, so the attempt claims nothing else.
        Assert.False(sent.TrialContent);
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
    public async Task Send_PostsTheNotificationBody_WhenNotOnTrial()
    {
        var handler = new CapturingHandler(_ => Queued("SM1"));

        await Provider(handler).SendAsync(Message(), Settings(), CancellationToken.None);

        Assert.Contains("To=%2B5511982254398", handler.LastBody);
        Assert.Contains("From=%2B17372212163", handler.LastBody);
        Assert.Contains("Seu+pedido+42", handler.LastBody);
    }

    [Fact]
    public async Task Send_PostsTheTemplateKey_WhenOnTrial()
    {
        var handler = new CapturingHandler(_ => Queued("SM1"));

        await Provider(handler).SendAsync(Message(), Settings(trial: true), CancellationToken.None);

        // A trial account refuses free text, so the key goes out and Twilio expands it on its side.
        Assert.Contains("Body=sms_account_alerts", handler.LastBody);
        Assert.DoesNotContain("Seu+pedido", handler.LastBody);
        Assert.Contains("To=%2B5511982254398", handler.LastBody);
    }

    [Fact]
    public async Task Send_ReportsTrialContent_WhenTheTemplateKeyReplacedTheBody()
    {
        var handler = new CapturingHandler(_ => Queued("SM1"));

        var outcome = await Provider(handler).SendAsync(Message(), Settings(trial: true), CancellationToken.None);

        // The persisted body never left, so an attempt recorded as a plain send would put a delivery in
        // the history that did not happen (ADR-028 item 2.1).
        Assert.True(Assert.IsType<SendOutcome.Sent>(outcome).TrialContent);
    }

    [Fact]
    public async Task Send_FailsPermanently_WhenTrialTemplateIsNotConfigured()
    {
        var handler = new CapturingHandler(_ => Queued("SM1"));

        var outcome = await Provider(handler).SendAsync(
            Message(), Settings(trial: true, trialTemplate: null), CancellationToken.None);

        Assert.IsType<SendOutcome.PermanentFailure>(outcome);
        Assert.Equal(0, handler.Calls);
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
    public async Task Send_CarriesTheProviderReason_WhenRecipientIsUnverified()
    {
        // The failure a trial account hits first: sending to a number nobody verified.
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
}
