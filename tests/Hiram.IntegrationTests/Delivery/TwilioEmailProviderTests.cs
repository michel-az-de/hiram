using System.Net;
using System.Text;
using System.Text.Json;
using Hiram.Application.Delivery;
using Hiram.Infrastructure.Delivery;

namespace Hiram.IntegrationTests.Delivery;

public class TwilioEmailProviderTests
{
    // The literal content the trial account approves, copied from the Console request. Anything else is
    // rejected while the account is on trial, which is exactly what the trial mode settings carry.
    private const string ApprovedSubject = "Your Order Has Been Confirmed!";
    private const string ApprovedHtml =
        "<p><b>This is a test email from Twilio.</b></p><h2>Thank you for your order!</h2>";

    private sealed class CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? LastBody { get; private set; }
        public string? LastAuthorization { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
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

    private static HttpResponseMessage Accepted(string operationId) =>
        new(HttpStatusCode.Accepted)
        {
            Content = new StringContent($$"""{"operationId":"{{operationId}}","operationLocation":"/Emails/Operations/{{operationId}}"}""")
        };

    private static HttpResponseMessage Rejected(HttpStatusCode status, string? message = null) =>
        new(status)
        {
            Content = new StringContent(
                message is null ? "{}" : $$"""{"code":400,"message":"{{message}}","status":400}""")
        };

    private static EmailProviderSettings Settings(
        string? from = "AC123@twilio.email",
        string? apiKeySid = "SK123",
        string? secret = "shh",
        bool trial = false,
        string? trialSubject = ApprovedSubject,
        string? trialHtml = ApprovedHtml)
    {
        var values = new Dictionary<string, string>();
        if (from is not null) values["from"] = from;
        if (apiKeySid is not null) values["api_key_sid"] = apiKeySid;
        if (trial)
        {
            values["trial_mode"] = "true";
            if (trialSubject is not null) values["trial_subject"] = trialSubject;
            if (trialHtml is not null) values["trial_html"] = trialHtml;
        }

        return new EmailProviderSettings(values, secret, ProviderConfigOrigin.Tenant);
    }

    private static EmailMessage Message() => new("ops@example.com", "Pedido 42 confirmado", "Seu pedido saiu para entrega.");

    private static TwilioEmailProvider Provider(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://comms.twilio.com/v1/") };
        return new TwilioEmailProvider(http);
    }

    [Fact]
    public async Task Send_ReturnsSentWithOperationId_WhenAccepted()
    {
        var handler = new CapturingHandler(_ => Accepted("comms_operation_01abc"));

        var outcome = await Provider(handler).SendAsync(Message(), Settings(), CancellationToken.None);

        var sent = Assert.IsType<SendOutcome.Sent>(outcome);
        Assert.Equal("comms_operation_01abc", sent.ProviderMessageId);

        // Outside trial the notification's own content went out, so the attempt claims nothing else.
        Assert.False(sent.TrialContent);
    }

    [Fact]
    public async Task Send_ReportsTrialContent_WhenTheApprovedMessageReplacedTheNotification()
    {
        var handler = new CapturingHandler(_ => Accepted("comms_operation_01abc"));

        var outcome = await Provider(handler).SendAsync(Message(), Settings(trial: true), CancellationToken.None);

        // The persisted body never left, so an attempt recorded as a plain send would put a delivery in
        // the history that did not happen (ADR-028 item 2.1).
        Assert.True(Assert.IsType<SendOutcome.Sent>(outcome).TrialContent);
    }

    [Fact]
    public async Task Send_AuthenticatesWithTheApiKey()
    {
        var handler = new CapturingHandler(_ => Accepted("comms_operation_01abc"));

        await Provider(handler).SendAsync(Message(), Settings(apiKeySid: "SK9", secret: "topsecret"), CancellationToken.None);

        var expected = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("SK9:topsecret"));
        Assert.Equal(expected, handler.LastAuthorization);
    }

    [Fact]
    public async Task Send_PostsTheNotificationContent_WhenNotOnTrial()
    {
        var handler = new CapturingHandler(_ => Accepted("comms_operation_01abc"));

        await Provider(handler).SendAsync(Message(), Settings(), CancellationToken.None);

        using var body = JsonDocument.Parse(handler.LastBody!);
        var content = body.RootElement.GetProperty("content");
        Assert.Equal("Pedido 42 confirmado", content.GetProperty("subject").GetString());
        Assert.Equal("Seu pedido saiu para entrega.", content.GetProperty("html").GetString());
        Assert.Equal("ops@example.com", body.RootElement.GetProperty("to")[0].GetProperty("address").GetString());
    }

    [Fact]
    public async Task Send_PostsTheApprovedContent_WhenOnTrial()
    {
        var handler = new CapturingHandler(_ => Accepted("comms_operation_01abc"));

        await Provider(handler).SendAsync(Message(), Settings(trial: true), CancellationToken.None);

        using var body = JsonDocument.Parse(handler.LastBody!);
        var content = body.RootElement.GetProperty("content");
        Assert.Equal(ApprovedSubject, content.GetProperty("subject").GetString());
        Assert.Equal(ApprovedHtml, content.GetProperty("html").GetString());

        // The notification's own text must not reach a trial account, which would reject the whole send.
        Assert.DoesNotContain("Seu pedido saiu para entrega.", handler.LastBody);
    }

    [Fact]
    public async Task Send_KeepsTheRealRecipient_WhenOnTrial()
    {
        var handler = new CapturingHandler(_ => Accepted("comms_operation_01abc"));

        await Provider(handler).SendAsync(Message(), Settings(trial: true), CancellationToken.None);

        using var body = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal("ops@example.com", body.RootElement.GetProperty("to")[0].GetProperty("address").GetString());
    }

    [Fact]
    public async Task Send_FailsPermanently_WhenTrialContentIsNotConfigured()
    {
        var handler = new CapturingHandler(_ => Accepted("comms_operation_01abc"));

        var outcome = await Provider(handler).SendAsync(
            Message(), Settings(trial: true, trialHtml: null), CancellationToken.None);

        Assert.IsType<SendOutcome.PermanentFailure>(outcome);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Send_FailsPermanently_WhenCredentialsAreMissing()
    {
        var handler = new CapturingHandler(_ => Accepted("comms_operation_01abc"));

        var outcome = await Provider(handler).SendAsync(Message(), Settings(secret: null), CancellationToken.None);

        Assert.IsType<SendOutcome.PermanentFailure>(outcome);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Send_CarriesTheProviderReason_WhenContentIsRejected()
    {
        // The real trial rejection, which is the failure an operator will actually hit first.
        var handler = new CapturingHandler(_ => Rejected(
            HttpStatusCode.BadRequest, "Invalid template: email content does not match any approved template"));

        var outcome = await Provider(handler).SendAsync(Message(), Settings(), CancellationToken.None);

        var failure = Assert.IsType<SendOutcome.PermanentFailure>(outcome);
        Assert.Contains("does not match any approved template", failure.Reason);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Send_IsTransient_WhenThrottledOrServerSide(HttpStatusCode status)
    {
        var handler = new CapturingHandler(_ => Rejected(status));

        var outcome = await Provider(handler).SendAsync(Message(), Settings(), CancellationToken.None);

        Assert.IsType<SendOutcome.TransientFailure>(outcome);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Send_IsPermanent_WhenTheRequestIsRefused(HttpStatusCode status)
    {
        var handler = new CapturingHandler(_ => Rejected(status));

        var outcome = await Provider(handler).SendAsync(Message(), Settings(), CancellationToken.None);

        Assert.IsType<SendOutcome.PermanentFailure>(outcome);
    }

    [Fact]
    public async Task Send_IsTransient_WhenTransportFails()
    {
        var outcome = await Provider(new ThrowingHandler(new HttpRequestException("connection reset")))
            .SendAsync(Message(), Settings(), CancellationToken.None);

        Assert.IsType<SendOutcome.TransientFailure>(outcome);
    }

    [Fact]
    public async Task Send_PropagatesCancellation_OnShutdown()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var handler = new ThrowingHandler(new TaskCanceledException());

        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            Provider(handler).SendAsync(Message(), Settings(), cancelled.Token));
    }
}
