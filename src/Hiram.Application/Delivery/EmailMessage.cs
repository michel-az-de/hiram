namespace Hiram.Application.Delivery;

public sealed record EmailMessage(string Recipient, string Subject, string Body);
