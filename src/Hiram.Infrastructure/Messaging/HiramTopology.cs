namespace Hiram.Infrastructure.Messaging;

public static class HiramTopology
{
    public const string Exchange = "hiram.notifications";
    public const string EmailQueue = "hiram.notifications.email";
    public const string EmailRoutingKey = "email";
    public const string PushQueue = "hiram.notifications.push";
    public const string PushRoutingKey = "push";

    public const string Dlx = "hiram.notifications.dlx";
    public const string DeadLetterQueue = "hiram.notifications.dead-letter";
    public const string DeadLetterRoutingKey = "dead-letter";
}
