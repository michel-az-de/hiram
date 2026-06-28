namespace Hiram.Application.Messaging;

public sealed class MessageDispatchGuard
{
    private readonly IMessageClaimStore _claims;

    public MessageDispatchGuard(IMessageClaimStore claims)
    {
        _claims = claims;
    }

    // Claims the message key before calling the provider. A replay or a broker redelivery presents the
    // same key, the claim fails, and the send is skipped: the provider is never called twice.
    public async Task<bool> TryDispatchAsync(Guid tenantId, string messageKey, Func<CancellationToken, Task> send, CancellationToken cancellationToken)
    {
        if (!await _claims.TryClaimAsync(tenantId, messageKey, cancellationToken))
            return false;

        await send(cancellationToken);
        return true;
    }
}
