namespace Hiram.Application.Delivery;

// No subject: SMS is a single body delivered to a phone number in E.164 format.
public sealed record SmsMessage(string Recipient, string Body);
