namespace Hiram.Infrastructure.Messaging;

public static class HiramTopology
{
    public const string Exchange = "hiram.notifications";
    public const string EmailQueue = "hiram.notifications.email";
    public const string EmailRoutingKey = "email";
    public const string PushQueue = "hiram.notifications.push";
    public const string PushRoutingKey = "push";
    public const string WebhookQueue = "hiram.notifications.webhook";
    public const string WebhookRoutingKey = "webhook";

    // Raw events land here for the routine engine to fan out. The consumer arrives in step 1.2; until
    // then the queue holds the messages instead of dropping them as unroutable.
    public const string EventQueue = "hiram.notifications.event";
    public const string EventRoutingKey = "event";

    public const string Dlx = "hiram.notifications.dlx";
    public const string DeadLetterQueue = "hiram.notifications.dead-letter";
    public const string DeadLetterRoutingKey = "dead-letter";
}
