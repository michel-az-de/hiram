using Hiram.Domain.Notifications;
using Hiram.Domain.Routines;

namespace Hiram.Application.Consents;

public sealed class ConsentPolicy
{
    private readonly IConsentStore _store;

    public ConsentPolicy(IConsentStore store)
    {
        _store = store;
    }

    public async Task<bool> IsAllowedAsync(
        Guid tenantId, Guid userId, NotificationChannel channel, NotificationCategory category, CancellationToken cancellationToken)
    {
        var consent = await _store.GetAsync(tenantId, userId, channel, category, cancellationToken);
        if (consent is not null)
            return consent.OptIn;

        // No explicit record: transactional and operational are allowed by legitimate interest; marketing
        // requires an explicit opt-in.
        return category != NotificationCategory.Marketing;
    }
}
