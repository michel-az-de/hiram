namespace Hiram.Contracts;

public sealed record SetConsentRequest(
    Guid UserId,
    string Channel,
    string Category,
    bool OptIn);
