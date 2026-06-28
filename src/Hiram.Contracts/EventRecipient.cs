namespace Hiram.Contracts;

// The contact travels in the event payload at emission time (ADR-018). The engine picks the address
// for the resolved channel: Email uses Email, Sms and WhatsApp use Phone, InApp uses UserId.
public sealed record EventRecipient(
    string? UserId = null,
    string? Email = null,
    string? Phone = null);
