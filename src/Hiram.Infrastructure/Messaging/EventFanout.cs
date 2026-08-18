using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hiram.Application.Abstractions;
using Hiram.Application.Events;
using Hiram.Application.Notifications;
using Hiram.Application.Routines;
using Hiram.Application.Templates;
using Hiram.Domain.Notifications;
using Hiram.Domain.Outbox;
using Hiram.Infrastructure.Telemetry;
using Microsoft.Extensions.Logging;

namespace Hiram.Infrastructure.Messaging;

// Turns one ingested event into the same rendered request and outbox row the direct path produces, one
// per resolved channel, so the existing queue, processor, retry, dead letter and shadow deliver it. The
// routine engine decides which channels fire; this fans that decision out into concrete messages.
public sealed class EventFanout
{
    private static readonly IReadOnlyDictionary<string, object?> EmptyData = new Dictionary<string, object?>();

    private readonly RoutineResolver _routines;
    private readonly ChannelResolver _channels;
    private readonly ITemplateStore _templates;
    private readonly ITemplateRenderer _renderer;
    private readonly INotificationStore _store;
    private readonly IClock _clock;
    private readonly ILogger<EventFanout> _logger;

    public EventFanout(
        RoutineResolver routines,
        ChannelResolver channels,
        ITemplateStore templates,
        ITemplateRenderer renderer,
        INotificationStore store,
        IClock clock,
        ILogger<EventFanout> logger)
    {
        _routines = routines;
        _channels = channels;
        _templates = templates;
        _renderer = renderer;
        _store = store;
        _clock = clock;
        _logger = logger;
    }

    public async Task FanOutAsync(OutboxEventPayload @event, CancellationToken cancellationToken)
    {
        var decision = await _routines.ResolveAsync(@event.TenantId, @event.EventType, cancellationToken);

        if (decision.NoRoute)
        {
            // No routine matched the event type: nothing to send, recorded and acked, not an error. The
            // counter is what makes it visible: an emitter sending a type nobody routes looks exactly like
            // a healthy system from the outside, and the log alone never reaches a dashboard.
            _logger.LogInformation(
                "Event {EventId} of type {EventType} matched no routine", @event.EventId, @event.EventType);
            HiramDiagnostics.EventsWithoutRoute.Add(
                1, new KeyValuePair<string, object?>("hiram.event_type", @event.EventType));
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

            // Every resolved channel has to land on an arm, including the ones with no sender. The chain
            // this replaces had no fallback, so push, which the admin API accepts and consent can allow,
            // fell through and produced nothing that any log or dashboard could show.
            await (item.Channel switch
            {
                NotificationChannel.Email => FanOutEmailAsync(@event, item, cancellationToken),
                NotificationChannel.Sms => FanOutSmsAsync(@event, item, cancellationToken),
                NotificationChannel.WhatsApp => FanOutWhatsAppAsync(@event, item, cancellationToken),
                _ => RecordUnsupportedChannel(@event, item.Channel),
            });
        }
    }

    // Not an error and not a retry: the event is acked either way, exactly like the no-route case above.
    // What changes is that the drop becomes countable, so the shadow parity stops disagreeing for a
    // reason nobody can see. The discard arm keeps a value outside the named members, which a stale
    // routine row can still carry, on this same recorded path instead of throwing mid fan-out.
    private Task RecordUnsupportedChannel(OutboxEventPayload @event, NotificationChannel channel)
    {
        _logger.LogWarning(
            "Event {EventId} routed to channel {Channel}, which has no fan-out, recorded and skipped",
            @event.EventId, channel);
        HiramDiagnostics.FanoutChannelUnsupported.Add(
            1,
            new KeyValuePair<string, object?>("hiram.channel", channel.ToString().ToLowerInvariant()),
            new KeyValuePair<string, object?>("hiram.event_type", @event.EventType));

        return Task.CompletedTask;
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

        if (template.Subject is null)
        {
            // Email renders the subject as the subject line and the domain refuses to create an email
            // template without one, so only a row written around the entity lands here. Deterministic, so
            // it is skipped like a failed render instead of retried forever.
            _logger.LogWarning(
                "Template {Template} carries no subject for event {EventId}, skipping", item.TemplateName, @event.EventId);
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

        await SaveFanoutAsync(
            @event, NotificationChannel.Email, recipient, subject, body, template.Version, cancellationToken);
    }

    private async Task FanOutSmsAsync(OutboxEventPayload @event, FanoutItem item, CancellationToken cancellationToken)
    {
        var recipient = @event.Payload.RecipientPhone;
        if (string.IsNullOrWhiteSpace(recipient))
        {
            // The routine wants SMS but the emission carried no number: a retry cannot fix a missing contact.
            _logger.LogWarning("Event {EventId} routed to sms without a recipient phone, skipping", @event.EventId);
            return;
        }

        if (!PhoneNumber.IsE164(recipient))
        {
            // The carrier refuses anything else, so writing the row would only buy a guaranteed failure
            // and a dead letter. The same rule the direct submit applies at the border.
            _logger.LogWarning(
                "Event {EventId} routed to sms with a recipient that is not E.164, skipping", @event.EventId);
            return;
        }

        var template = await _templates.FindByNameAsync(@event.TenantId, NotificationChannel.Sms, item.TemplateName, cancellationToken);
        if (template is null)
        {
            // Approval said the template existed; a delete between resolve and render lands here, not a send.
            _logger.LogWarning(
                "Template {Template} vanished before rendering event {EventId}, skipping", item.TemplateName, @event.EventId);
            return;
        }

        string body;
        try
        {
            body = _renderer.Render(template.Body, @event.Payload.Data ?? EmptyData);
        }
        catch (TemplateRenderException ex)
        {
            _logger.LogWarning(
                ex, "Template {Template} failed to render for event {EventId}, skipping", item.TemplateName, @event.EventId);
            return;
        }

        // An SMS has no subject line, so none is rendered and none is stored.
        await SaveFanoutAsync(
            @event, NotificationChannel.Sms, recipient.Trim(), subject: null, body, template.Version, cancellationToken);
    }

    private async Task FanOutWhatsAppAsync(OutboxEventPayload @event, FanoutItem item, CancellationToken cancellationToken)
    {
        // Consent already decided this channel above, and on WhatsApp that gate is fail-closed in every
        // category: reaching here means an explicit opt-in exists, so nothing is re-checked.
        var recipient = @event.Payload.RecipientPhone;
        if (string.IsNullOrWhiteSpace(recipient))
        {
            // The routine wants WhatsApp but the emission carried no number: a retry cannot fix a missing contact.
            _logger.LogWarning("Event {EventId} routed to whatsapp without a recipient phone, skipping", @event.EventId);
            return;
        }

        if (!PhoneNumber.IsE164(recipient))
        {
            // The provider refuses anything else, so writing the row would only buy a guaranteed failure
            // and a dead letter. The same rule the direct submit applies at the border.
            _logger.LogWarning(
                "Event {EventId} routed to whatsapp with a recipient that is not E.164, skipping", @event.EventId);
            return;
        }

        var template = await _templates.FindByNameAsync(@event.TenantId, NotificationChannel.WhatsApp, item.TemplateName, cancellationToken);
        if (template is null)
        {
            // Approval said the template existed; a delete between resolve and render lands here, not a send.
            _logger.LogWarning(
                "Template {Template} vanished before rendering event {EventId}, skipping", item.TemplateName, @event.EventId);
            return;
        }

        string body;
        try
        {
            body = _renderer.Render(template.Body, @event.Payload.Data ?? EmptyData);
        }
        catch (TemplateRenderException ex)
        {
            _logger.LogWarning(
                ex, "Template {Template} failed to render for event {EventId}, skipping", item.TemplateName, @event.EventId);
            return;
        }

        // A WhatsApp message has no subject line, so none is rendered and none is stored. The recipient
        // stays a bare number: the "whatsapp:" prefix is assembled by the adapter at send time.
        await SaveFanoutAsync(
            @event, NotificationChannel.WhatsApp, recipient.Trim(), subject: null, body, template.Version, cancellationToken);
    }

    // The rendered message becomes a request and its outbox row in one transaction, the founding
    // invariant. Every channel lands here, so the message key, the payload and the routing key stay
    // identical across them and only the rendering above differs.
    private async Task SaveFanoutAsync(
        OutboxEventPayload @event,
        NotificationChannel channel,
        string recipient,
        string? subject,
        string body,
        int templateVersion,
        CancellationToken cancellationToken)
    {
        var messageKey = MessageKey(@event.EventId, channel, recipient, templateVersion);
        var now = _clock.UtcNow;
        var notificationId = Guid.NewGuid();

        var request = new NotificationRequest(
            notificationId, @event.TenantId, channel, recipient, subject, body, now, messageKey);

        var payload = new OutboxNotificationPayload(
            notificationId, @event.TenantId, channel.ToString(), recipient, subject, body);

        // The outbox type is what OutboxMessageDispatcher routes on, and it keys on the lowercase name.
        var outbox = new OutboxMessage(
            Guid.NewGuid(), @event.TenantId, Wire(channel), JsonSerializer.Serialize(payload), now, Activity.Current?.Id);

        try
        {
            await _store.SaveAsync(request, outbox, cancellationToken);
        }
        catch (DuplicateIdempotencyKeyException)
        {
            // The deterministic message key already produced this exact message: a redelivery or replay of
            // the event, not a new send. The unique index is the arbiter, so drop it and let the worker ack.
            _logger.LogInformation(
                "Event {EventId} {Channel} to {Recipient} already fanned out, skipping",
                @event.EventId, Wire(channel), recipient);
        }
    }

    private static string Wire(NotificationChannel channel) => channel.ToString().ToLowerInvariant();

    // ADR-017 message key: hash(event_id, channel, recipient, template_version). Transactional events have
    // no schedule slot, so the slot collapses and the key degenerates to these four fields. Frozen with the
    // message, never recomputed at delivery, so a redelivery or replay resolves to the same row.
    private static string MessageKey(string eventId, NotificationChannel channel, string recipient, int templateVersion)
    {
        var canonical = $"{eventId}\n{Wire(channel)}\n{recipient}\n{templateVersion}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
