namespace Hiram.Contracts;

// The contact travels in the event payload at emission time (ADR-018). Email uses Email. Phone and
// UserId remain in the contract for source compatibility with emitters while no core channel consumes them.
public sealed record EventRecipient(
    string? UserId = null,
    string? Email = null,
    string? Phone = null);
