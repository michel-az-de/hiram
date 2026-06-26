using System.Diagnostics;
using System.Text.Json;
using Hiram.Application.Abstractions;
using Hiram.Application.Webhooks;
using Hiram.Domain.Notifications;
using Hiram.Domain.Outbox;
using Hiram.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hiram.Infrastructure.Webhooks;

internal static class WebhookOutbox
{
    public const string MessageType = "webhook";

    // Enqueues a status event on the same transaction as the status change, but only when the tenant has at
    // least one endpoint, so a webhook event never exists without an endpoint to deliver it to.
    public static async Task TryEnqueueAsync(
        HiramDbContext context, NotificationRequest notification, IClock clock, CancellationToken cancellationToken)
    {
        var status = notification.Status switch
        {
            NotificationStatus.Sent => "sent",
            NotificationStatus.DeadLettered => "dead_lettered",
            _ => (string?)null
        };
        if (status is null)
            return;

        if (!await context.WebhookEndpoints.AnyAsync(w => w.TenantId == notification.TenantId, cancellationToken))
            return;

        var payload = new WebhookOutboxPayload(
            notification.TenantId,
            notification.Id,
            notification.Channel.ToString().ToLowerInvariant(),
            status,
            clock.UtcNow);

        context.OutboxMessages.Add(new OutboxMessage(
            Guid.NewGuid(), notification.TenantId, MessageType, JsonSerializer.Serialize(payload), clock.UtcNow, Activity.Current?.Id));
    }
}
