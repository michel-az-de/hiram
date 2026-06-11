namespace Hiram.Contracts;

public sealed record SubmitNotificationRequest(string Channel, string Recipient, string Subject, string Body);
