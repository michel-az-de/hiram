namespace Hiram.Application.Messaging;

public interface IMessageClaimStore
{
    // True when the key was newly claimed (proceed to send); false when already claimed (replay or
    // redelivery, do not resend). The durable arbiter is a unique index in Postgres.
    Task<bool> TryClaimAsync(Guid tenantId, string messageKey, CancellationToken cancellationToken);
}
