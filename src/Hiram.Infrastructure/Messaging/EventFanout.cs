using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hiram.Application.Abstractions;
using Hiram.Application.Events;
using Hiram.Application.Metering;
using Hiram.Application.Notifications;
using Hiram.Application.Routines;
using Hiram.Application.Templates;
using Hiram.Domain.Metering;
using Hiram.Domain.Notifications;
using Hiram.Domain.Outbox;
using Hiram.Infrastructure.Telemetry;
using Microsoft.Extensions.Logging;

namespace Hiram.Infrastructure.Messaging;

// Turns one ingested event into the same rendered request and outbox row the direct path produces, one
// per resolved channel, so the existing relay, consumer, retry, dead letter and shadow deliver it. The
// routine engine decides which channels fire; this fans that decision out into concrete messages.
public sealed class EventFanout
{
    private static readonly IReadOnlyDictionary<string, object?> EmptyData = new Dictionary<string, object?>();

    private readonly RoutineResolver _routines;
    private readonly ChannelResolver _channels;
    private readonly ITemplateStore _templates;
    private readonly ITemplateRenderer _renderer;
    private readonly INotificationStore _store;
    private readonly ICreditCalculator _calculator;
    private readonly IClock _clock;
    private readonly ILogger<EventFanout> _logger;

    public EventFanout(
        RoutineResolver routines,
        ChannelResolver channels,
        ITemplateStore templates,
        ITemplateRenderer renderer,
        INotificationStore store,
        ICreditCalculator calculator,
        IClock clock,
        ILogger<EventFanout> logger)
    {
        _routines = routines;
        _channels = channels;
        _templates = templates;
        _renderer = renderer;
        _store = store;
        _calculator = calculator;
        _clock = clock;
        _logger = logger;
    }

    public async Task FanOutAsync(OutboxEventPayload @event, CancellationToken cancellationToken)
    {
        var decision = await _routines.ResolveAsync(@event.TenantId, @event.EventType, cancellationToken);

        if (decision.NoRoute)
        {
            // No routine matched the event type: nothing to send, recorded and acked, not an error.
            _logger.LogInformation(
                "Event {EventId} of type {EventType} matched no routine", @event.EventId, @event.EventType);
            return;
        }

        foreach (var item in decision.Suppressed)
            _logger.LogInformation(
                "Event {EventId} suppressed on channel {Channel}: {Reason}", @event.EventId, item.Channel, item.Reason);

        // The contact's user id decides consent; a missing RecipientUserId falls open for transactional and
        // operational and closed for marketing (ADR-024).
        var recipientUserId = Guid.TryParse(@event.Payload.RecipientUserId, out var parsed) ? parsed : (Guid?)null;
        var now = _clock.UtcNow;

        foreach (var item in decision.Fanout)
        {
            var allowed = await _channels.ResolveAsync(
                @event.TenantId, recipientUserId, item.Routine.Category, [item.Channel], now, cancellationToken);

            if (allowed.Count == 0)
            {
                // Consent (or an active kill-switch) denies this channel: no request is written, so the send
                // never happens. Recorded for the shadow suppression rate, never a silent drop.
                _logger.LogInformation(
                    "Event {EventId} suppressed on channel {Channel} by consent", @event.EventId, item.Channel);
                HiramDiagnostics.NotificationsSuppressed.Add(
                    1,
                    new KeyValuePair<string, object?>("hiram.reason", "consent"),
                    new KeyValuePair<string, object?>("hiram.channel", item.Channel.ToString().ToLowerInvariant()));
                continue;
            }

            // Only email is wired in this slice; the other channels fan out in their own waves.
            if (item.Channel == NotificationChannel.Email)
                await FanOutEmailAsync(@event, item, cancellationToken);
        }
    }

    private async Task FanOutEmailAsync(OutboxEventPayload @event, FanoutItem item, CancellationToken cancellationToken)
    {
        var recipient = @event.Payload.RecipientEmail;
        if (string.IsNullOrWhiteSpace(recipient))
        {
            // The routine wants email but the emission carried no address: a retry cannot fix a missing contact.
            _logger.LogWarning("Event {EventId} routed to email without a recipient address, skipping", @event.EventId);
            return;
        }

        var template = await _templates.FindByNameAsync(@event.TenantId, NotificationChannel.Email, item.TemplateName, cancellationToken);
        if (template is null)
        {
            // Approval said the template existed; a delete between resolve and render lands here, not a send.
            _logger.LogWarning(
                "Template {Template} vanished before rendering event {EventId}, skipping", item.TemplateName, @event.EventId);
            return;
        }

        string subject;
        string body;
        try
        {
            var data = @event.Payload.Data ?? EmptyData;
            subject = _renderer.Render(template.Subject, data);
            body = _renderer.Render(template.Body, data);
        }
        catch (TemplateRenderException ex)
        {
            // A bad template or missing data is deterministic: a retry cannot fix it, so record and skip
            // instead of requeuing forever. A durable suppression record arrives with the parity work.
            _logger.LogWarning(
                ex, "Template {Template} failed to render for event {EventId}, skipping", item.TemplateName, @event.EventId);
            return;
        }

        var messageKey = MessageKey(@event.EventId, NotificationChannel.Email, recipient, template.Version);
        var now = _clock.UtcNow;
        var notificationId = Guid.NewGuid();

        var request = new NotificationRequest(
            notificationId, @event.TenantId, NotificationChannel.Email, recipient, subject, body, now, messageKey);

        var payload = new OutboxNotificationPayload(
            notificationId, @event.TenantId, NotificationChannel.Email.ToString(), recipient, subject, body);

        var outbox = new OutboxMessage(
            Guid.NewGuid(), @event.TenantId, "email", JsonSerializer.Serialize(payload), now, Activity.Current?.Id);

        var payloadBytes = Encoding.UTF8.GetByteCount(subject) + Encoding.UTF8.GetByteCount(body);
        var cost = _calculator.Cost(NotificationChannel.Email, payloadBytes);
        var ledgerEntry = CreditLedgerEntry.Debit(@event.TenantId, notificationId, cost, "event:fanout", now);

        try
        {
            await _store.SaveAsync(request, outbox, ledgerEntry, cancellationToken);
        }
        catch (DuplicateIdempotencyKeyException)
        {
            // The deterministic message key already produced this exact message: a redelivery or replay of
            // the event, not a new send. The unique index is the arbiter, so drop it and let the worker ack.
            _logger.LogInformation(
                "Event {EventId} email to {Recipient} already fanned out, skipping", @event.EventId, recipient);
        }
    }

    // ADR-017 message key: hash(event_id, channel, recipient, template_version). Transactional events have
    // no schedule slot, so the slot collapses and the key degenerates to these four fields. Frozen with the
    // message, never recomputed at delivery, so a redelivery or replay resolves to the same row.
    private static string MessageKey(string eventId, NotificationChannel channel, string recipient, int templateVersion)
    {
        var canonical = $"{eventId}\n{channel.ToString().ToLowerInvariant()}\n{recipient}\n{templateVersion}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
