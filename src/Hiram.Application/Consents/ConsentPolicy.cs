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

        // WhatsApp has no legitimate-interest default: Meta's policy requires an explicit opt-in for every
        // category, so an absent record denies.
        if (channel == NotificationChannel.WhatsApp)
            return false;

        // For the other channels an absent record allows transactional and operational by legitimate
        // interest; only marketing requires an explicit opt-in.
        return category != NotificationCategory.Marketing;
    }
}
