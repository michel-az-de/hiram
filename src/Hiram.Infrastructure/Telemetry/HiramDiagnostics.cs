using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Hiram.Infrastructure.Telemetry;

public static class HiramDiagnostics
{
    public const string MessagingSourceName = "Hiram.Messaging";
    public const string MeterName = "Hiram.Notifications";

    public static readonly ActivitySource Messaging = new(MessagingSourceName);

    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> NotificationsAccepted = Meter.CreateCounter<long>("hiram.notifications.accepted");
    public static readonly Counter<long> OutboxDispatched = Meter.CreateCounter<long>("hiram.outbox.dispatched");
    public static readonly Counter<long> NotificationsShadowed = Meter.CreateCounter<long>("hiram.notifications.shadowed");
    public static readonly Counter<long> NotificationsSent = Meter.CreateCounter<long>("hiram.notifications.sent");
    public static readonly Counter<long> NotificationsFailed = Meter.CreateCounter<long>("hiram.notifications.failed");
    public static readonly Counter<long> NotificationsDeadLettered = Meter.CreateCounter<long>("hiram.notifications.dead_lettered");
    public static readonly Counter<long> Poisoned = Meter.CreateCounter<long>("hiram.notifications.poisoned");
    public static readonly Counter<long> NotificationsReplayed = Meter.CreateCounter<long>("hiram.notifications.replayed");
    public static readonly Counter<long> WebhooksDelivered = Meter.CreateCounter<long>("hiram.webhooks.delivered");
    public static readonly Counter<long> WebhooksFailed = Meter.CreateCounter<long>("hiram.webhooks.failed");
    public static readonly Counter<long> IdempotencyReplays = Meter.CreateCounter<long>("hiram.idempotency.replays");
    public static readonly Counter<long> SmtpDestinationRejected = Meter.CreateCounter<long>("hiram.smtp.destination_rejected");
    public static readonly Histogram<double> SendDuration = Meter.CreateHistogram<double>("hiram.send.duration", unit: "ms");
}
