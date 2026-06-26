using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Hiram.Application.Abstractions;
using Hiram.Application.Delivery;
using Hiram.Application.Tenancy;
using Hiram.Application.Webhooks;
using Hiram.Domain.Webhooks;
using Hiram.Infrastructure.Telemetry;
using Hiram.Infrastructure.Webhooks;
using Microsoft.Extensions.Logging;
using Polly;

namespace Hiram.Infrastructure.Messaging;

public sealed class WebhookDeliveryProcessor
{
    private static readonly TimeSpan PerAttemptTimeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient _http;
    private readonly IWebhookEndpointStore _endpoints;
    private readonly ISecretProtector _protector;
    private readonly ResiliencePipeline<SendOutcome> _pipeline;
    private readonly ILogger<WebhookDeliveryProcessor> _logger;

    public WebhookDeliveryProcessor(
        HttpClient http,
        IWebhookEndpointStore endpoints,
        ISecretProtector protector,
        ResiliencePipeline<SendOutcome> pipeline,
        ILogger<WebhookDeliveryProcessor> logger)
    {
        _http = http;
        _endpoints = endpoints;
        _protector = protector;
        _pipeline = pipeline;
        _logger = logger;
    }

    public async Task ProcessAsync(ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
    {
        WebhookOutboxPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<WebhookOutboxPayload>(body.Span);
        }
        catch (JsonException ex)
        {
            throw new PoisonMessageException("Webhook payload is not valid JSON.", ex);
        }

        if (payload is null)
            throw new PoisonMessageException("Webhook payload is empty.");

        using var logScope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["tenant_id"] = payload.TenantId,
            ["notification_id"] = payload.NotificationId
        });

        var endpoints = await _endpoints.ListAsync(payload.TenantId, cancellationToken);
        if (endpoints.Count == 0)
            // The tenant removed its endpoints between emission and delivery: nothing to do, let the worker ack.
            return;

        var eventBody = JsonSerializer.SerializeToUtf8Bytes(
            new WebhookEvent(payload.NotificationId, payload.Channel, payload.Status, payload.OccurredAt));

        foreach (var endpoint in endpoints)
        {
            var secret = _protector.Unprotect(endpoint.SecretProtected);
            var outcome = await _pipeline.ExecuteAsync(
                async token => await PostOnceAsync(endpoint, eventBody, secret, token, cancellationToken),
                cancellationToken);

            if (outcome is SendOutcome.Sent)
            {
                HiramDiagnostics.WebhooksDelivered.Add(1);
            }
            else
            {
                HiramDiagnostics.WebhooksFailed.Add(1);
                _logger.LogWarning("Webhook delivery to {Url} failed after retries", endpoint.Url);
            }
        }
    }

    private async Task<SendOutcome> PostOnceAsync(
        WebhookEndpoint endpoint, byte[] body, string secret, CancellationToken pipelineToken, CancellationToken outerToken)
    {
        using var activity = HiramDiagnostics.Messaging.StartActivity("deliver webhook", ActivityKind.Client);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(pipelineToken);
        timeout.CancelAfter(PerAttemptTimeout);

        try
        {
            using var content = new ByteArrayContent(body);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint.Url) { Content = content };
            request.Headers.TryAddWithoutValidation("X-Hiram-Signature", WebhookSignature.Compute(secret, body));

            using var response = await _http.SendAsync(request, timeout.Token);
            if (response.IsSuccessStatusCode)
                return new SendOutcome.Sent();

            return (int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests
                ? new SendOutcome.TransientFailure($"Webhook endpoint returned {(int)response.StatusCode}.")
                : new SendOutcome.PermanentFailure($"Webhook endpoint returned {(int)response.StatusCode}.");
        }
        catch (OperationCanceledException) when (!outerToken.IsCancellationRequested)
        {
            return new SendOutcome.TransientFailure($"Webhook exceeded the {PerAttemptTimeout.TotalSeconds:0}s attempt timeout.");
        }
        catch (HttpRequestException ex)
        {
            return new SendOutcome.TransientFailure(ex.Message);
        }
    }
}
