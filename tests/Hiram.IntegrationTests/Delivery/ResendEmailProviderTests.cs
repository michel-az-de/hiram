using System.Net;
using System.Text;
using Hiram.Application.Delivery;
using Hiram.Infrastructure.Delivery;

namespace Hiram.IntegrationTests.Delivery;

public class ResendEmailProviderTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(responder(request));
        }
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw exception;
    }

    private sealed class DelayHandler(TimeSpan delay) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(delay, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private static EmailProviderSettings Settings() =>
        new(new Dictionary<string, string> { ["from"] = "no-reply@hiram.dev" }, Secret: "re_123");

    private static EmailMessage Message() => new("ops@example.com", "hello", "f1 body");

    private static ResendEmailProvider Provider(HttpMessageHandler handler, TimeSpan? timeout = null)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.resend.com/") };
        if (timeout is { } value)
            http.Timeout = value;
        return new ResendEmailProvider(http);
    }

    [Fact]
    public async Task Send_ReturnsSent_OnSuccess()
    {
        var provider = Provider(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));

        var outcome = await provider.SendAsync(Message(), Settings(), CancellationToken.None);

        Assert.IsType<SendOutcome.Sent>(outcome);
    }

    [Fact]
    public async Task Send_CapturesProviderMessageId_FromResponseBody()
    {
        var provider = Provider(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"id":"re_abc123"}""", Encoding.UTF8, "application/json")
        }));

        var outcome = await provider.SendAsync(Message(), Settings(), CancellationToken.None);

        var sent = Assert.IsType<SendOutcome.Sent>(outcome);
        Assert.Equal("re_abc123", sent.ProviderMessageId);
    }

    [Fact]
    public async Task Send_ReturnsSentWithoutId_WhenBodyHasNone()
    {
        // A 2xx with no parseable id is still a send: the correlation handle degrades to null, it does not fail.
        var provider = Provider(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("", Encoding.UTF8, "application/json")
        }));

        var outcome = await provider.SendAsync(Message(), Settings(), CancellationToken.None);

        var sent = Assert.IsType<SendOutcome.Sent>(outcome);
        Assert.Null(sent.ProviderMessageId);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Send_ReturnsTransient_OnRetryableStatus(HttpStatusCode status)
    {
        var provider = Provider(new StubHandler(_ => new HttpResponseMessage(status)));

        var outcome = await provider.SendAsync(Message(), Settings(), CancellationToken.None);

        Assert.IsType<SendOutcome.TransientFailure>(outcome);
    }

    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task Send_ReturnsPermanent_OnValidationOrAuthError(HttpStatusCode status)
    {
        var provider = Provider(new StubHandler(_ => new HttpResponseMessage(status)));

        var outcome = await provider.SendAsync(Message(), Settings(), CancellationToken.None);

        Assert.IsType<SendOutcome.PermanentFailure>(outcome);
    }

    [Fact]
    public async Task Send_ReturnsTransient_OnTransportError()
    {
        var provider = Provider(new ThrowingHandler(new HttpRequestException("connection reset")));

        var outcome = await provider.SendAsync(Message(), Settings(), CancellationToken.None);

        Assert.IsType<SendOutcome.TransientFailure>(outcome);
    }

    [Fact]
    public async Task Send_ReturnsTransient_OnTimeout()
    {
        var provider = Provider(new DelayHandler(TimeSpan.FromSeconds(30)), timeout: TimeSpan.FromMilliseconds(100));

        var outcome = await provider.SendAsync(Message(), Settings(), CancellationToken.None);

        Assert.IsType<SendOutcome.TransientFailure>(outcome);
    }

    [Fact]
    public async Task Send_ReturnsPermanent_WhenMisconfigured()
    {
        var provider = Provider(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));

        var outcome = await provider.SendAsync(
            Message(),
            new EmailProviderSettings(new Dictionary<string, string>(), Secret: null),
            CancellationToken.None);

        Assert.IsType<SendOutcome.PermanentFailure>(outcome);
    }

    [Fact]
    public async Task Send_AuthenticatesWithBearerAndPostsToEmails()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var provider = Provider(handler);

        await provider.SendAsync(Message(), Settings(), CancellationToken.None);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("https://api.resend.com/emails", handler.LastRequest.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.LastRequest.Headers.Authorization!.Scheme);
        Assert.Equal("re_123", handler.LastRequest.Headers.Authorization.Parameter);
    }
}
