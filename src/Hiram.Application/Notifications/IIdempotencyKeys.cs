namespace Hiram.Application.Notifications;

// Redis backed fast path for idempotency. The durable guarantee is the partial unique index in Postgres;
// this only short circuits replays so the common case never reaches the database.
public interface IIdempotencyKeys
{
    // Claims the key for this notification id. Returns the existing notification id when the key was
    // already claimed (a replay), or null when this call took the claim.
    Task<Guid?> TryClaimAsync(Guid tenantId, string key, Guid notificationId, CancellationToken cancellationToken);

    // Overwrites the cached id, used to repair a stale claim after the database resolves a conflict.
    Task StoreAsync(Guid tenantId, string key, Guid notificationId, CancellationToken cancellationToken);

    // Frees a claim whose notification failed to persist, so a retry with the same key can proceed.
    Task ReleaseAsync(Guid tenantId, string key, CancellationToken cancellationToken);
}
