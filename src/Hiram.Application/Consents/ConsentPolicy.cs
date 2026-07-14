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
        Guid tenantId, Guid? userId, NotificationChannel channel, NotificationCategory category, CancellationToken cancellationToken)
    {
        // With a user in hand, an explicit record is authoritative. Without one (the event carried no
        // RecipientUserId), there is nothing to look up, so the same default-by-category rule applies.
        if (userId is { } id)
        {
            var consent = await _store.GetAsync(tenantId, id, channel, category, cancellationToken);
            if (consent is not null)
                return consent.OptIn;
        }

        // WhatsApp has no legitimate-interest default: Meta's policy requires an explicit opt-in for every
        // category, so an absent record or an absent user denies.
        if (channel == NotificationChannel.WhatsApp)
            return false;

        // For the other channels this default allows transactional and operational by legitimate interest;
        // only marketing requires an explicit opt-in (fail-closed).
        return category != NotificationCategory.Marketing;
    }
}
