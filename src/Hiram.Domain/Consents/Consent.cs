using Hiram.Domain.Notifications;
using Hiram.Domain.Routines;

namespace Hiram.Domain.Consents;

// LGPD consent per user, channel and category. Owned by Hiram after the consent cutover (ADR-018);
// during shadow it is kept in sync with the EasyStok store by dual-write.
public sealed class Consent
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public NotificationChannel Channel { get; private set; }
    public NotificationCategory Category { get; private set; }
    public bool OptIn { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    private Consent()
    {
    }

    public Consent(
        Guid id,
        Guid tenantId,
        Guid userId,
        NotificationChannel channel,
        NotificationCategory category,
        bool optIn,
        DateTimeOffset updatedAtUtc)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Consent id is required.", nameof(id));
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        if (userId == Guid.Empty)
            throw new ArgumentException("User id is required.", nameof(userId));

        Id = id;
        TenantId = tenantId;
        UserId = userId;
        Channel = channel;
        Category = category;
        OptIn = optIn;
        UpdatedAtUtc = updatedAtUtc;
    }

    public void Set(bool optIn, DateTimeOffset updatedAtUtc)
    {
        OptIn = optIn;
        UpdatedAtUtc = updatedAtUtc;
    }
}
