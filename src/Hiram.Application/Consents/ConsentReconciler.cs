using Hiram.Domain.Consents;

namespace Hiram.Application.Consents;

public sealed class ConsentReconciler
{
    private readonly IConsentStore _store;

    public ConsentReconciler(IConsentStore store)
    {
        _store = store;
    }

    public async Task<bool> ReconcileAsync(Guid tenantId, ConsentSnapshot local, CancellationToken cancellationToken)
    {
        var current = await _store.GetAsync(tenantId, local.UserId, local.Channel, local.Category, cancellationToken);

        // Last write wins by timestamp: only converge when the snapshot is newer than what Hiram holds.
        if (current is not null && current.UpdatedAtUtc >= local.UpdatedAtUtc)
            return false;

        if (current is null)
        {
            await _store.UpsertAsync(
                new Consent(Guid.NewGuid(), tenantId, local.UserId, local.Channel, local.Category, local.OptIn, local.UpdatedAtUtc),
                cancellationToken);
        }
        else
        {
            current.Set(local.OptIn, local.UpdatedAtUtc);
            await _store.UpsertAsync(current, cancellationToken);
        }

        return true;
    }
}
