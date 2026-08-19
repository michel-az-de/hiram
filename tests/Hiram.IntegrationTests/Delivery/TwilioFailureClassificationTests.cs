using System.Net;
using System.Text;
using Hiram.Application.Delivery;
using Hiram.Infrastructure.Delivery;

namespace Hiram.IntegrationTests.Delivery;

// Classifying by status range gets some provider codes right by accident and others wrong. 30007 is the
// one that costs: the carrier filtered the message as spam, and retrying it raises the sender's spam
// score. 30003 is the mirror image, a device that was unreachable for a while and is worth another try.
public class TwilioFailureClassificationTests
{
    private sealed class CannedHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
    }

    private static SmsProviderSettings Settings() =>
        new(
            new Dictionary<string, string>
            {
                ["account_sid"] = "AC123",
                ["from"] = "+15005550006",
                ["api_key_sid"] = "SK123"
            },
            "shh",
            ProviderConfigOrigin.Tenant);

    private static async Task<SendOutcome> SendAsync(HttpStatusCode status, string body)
    {
        var http = new HttpClient(new CannedHandler(status, body)) { BaseAddress = new Uri("https://api.twilio.com/") };
        return await new TwilioSmsProvider(http).SendAsync(
            new SmsMessage("+5511999990000", "Seu pedido saiu para entrega."), Settings(), CancellationToken.None);
    }

    private static Task<SendOutcome> RejectedAsync(int code, string message) =>
        SendAsync(HttpStatusCode.BadRequest, $$"""{"code":{{code}},"message":"{{message}}"}""");

    // Carrier verdicts do not arrive as a rejected request: Twilio accepts the message and reports the
    // failure on the resource itself, with the code in error_code.
    private static Task<SendOutcome> ReportedAsync(int code, string message) =>
        SendAsync(
            HttpStatusCode.Created,
            $$"""{"sid":"SM1","status":"undelivered","error_code":{{code}},"error_message":"{{message}}"}""");

    [Fact]
    public async Task CarrierFiltering_IsPermanent_AndNeverRetried()
    {
        var outcome = await ReportedAsync(30007, "Message filtered");

        var failure = Assert.IsType<SendOutcome.PermanentFailure>(outcome);
        Assert.Contains("30007", failure.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnreachableDestination_IsRetryable()
    {
        var outcome = await ReportedAsync(30003, "Unreachable destination handset");

        var transient = Assert.IsType<SendOutcome.TransientFailure>(outcome);
        Assert.Contains("30003", transient.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NonExistentNumber_IsPermanent_AndNamesTheDestinationAsInvalid()
    {
        var outcome = await ReportedAsync(30005, "Unknown destination handset");

        var failure = Assert.IsType<SendOutcome.PermanentFailure>(outcome);
        Assert.Equal(DeliveryFailureKind.InvalidDestination, failure.Kind);
    }

    [Theory]
    [InlineData(21408, "Permission to send an SMS has not been enabled for the region")]
    [InlineData(30034, "Message from an unregistered number")]
    public async Task AccountMisconfiguration_IsPermanent_AndDistinguishableFromARecipientProblem(int code, string message)
    {
        var outcome = await RejectedAsync(code, message);

        var failure = Assert.IsType<SendOutcome.PermanentFailure>(outcome);
        Assert.Equal(DeliveryFailureKind.Configuration, failure.Kind);
    }

    [Fact]
    public async Task OptedOutRecipient_IsPermanent_AndDistinguishableFromAGenericRejection()
    {
        var outcome = await RejectedAsync(21610, "Attempt to send to unsubscribed recipient");

        var failure = Assert.IsType<SendOutcome.PermanentFailure>(outcome);
        Assert.Equal(DeliveryFailureKind.RecipientOptedOut, failure.Kind);
    }

    [Fact]
    public async Task UnknownRejection_StaysGeneric()
    {
        // A code the policy does not know must not be dressed up as something it is not.
        var outcome = await RejectedAsync(21212, "Invalid From number");

        var failure = Assert.IsType<SendOutcome.PermanentFailure>(outcome);
        Assert.Equal(DeliveryFailureKind.Provider, failure.Kind);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task RateLimitAndProviderOutage_StayTransient(HttpStatusCode status)
    {
        var outcome = await SendAsync(status, """{"code":20429,"message":"Too Many Requests"}""");

        Assert.IsType<SendOutcome.TransientFailure>(outcome);
    }

    [Fact]
    public async Task ReportedFailureWithoutACode_StaysPermanentAndGeneric()
    {
        var outcome = await SendAsync(
            HttpStatusCode.Created, """{"sid":"SM1","status":"failed","error_message":"Something went wrong"}""");

        var failure = Assert.IsType<SendOutcome.PermanentFailure>(outcome);
        Assert.Equal(DeliveryFailureKind.Provider, failure.Kind);
    }
}
