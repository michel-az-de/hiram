using Hiram.Domain.Consents;
using Hiram.Domain.Notifications;
using Hiram.Domain.Routines;

namespace Hiram.Application.Consents;

public interface IConsentStore
{
    Task<Consent?> GetAsync(Guid tenantId, Guid userId, NotificationChannel channel, NotificationCategory category, CancellationToken cancellationToken);

    Task UpsertAsync(Consent consent, CancellationToken cancellationToken);
}
