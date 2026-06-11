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
}
