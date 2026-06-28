using Hiram.Application.Consents;
using Hiram.Domain.Consents;
using Hiram.Domain.Notifications;
using Hiram.Domain.Routines;
using Microsoft.EntityFrameworkCore;

namespace Hiram.Infrastructure.Persistence;

public sealed class ConsentStore : IConsentStore
{
    private readonly HiramDbContext _context;

    public ConsentStore(HiramDbContext context)
    {
        _context = context;
    }

    public Task<Consent?> GetAsync(Guid tenantId, Guid userId, NotificationChannel channel, NotificationCategory category, CancellationToken cancellationToken) =>
        _context.Consents
            .AsNoTracking()
            .FirstOrDefaultAsync(
                c => c.TenantId == tenantId && c.UserId == userId && c.Channel == channel && c.Category == category,
                cancellationToken);

    public async Task UpsertAsync(Consent consent, CancellationToken cancellationToken)
    {
        var existing = await _context.Consents.FirstOrDefaultAsync(
            c => c.TenantId == consent.TenantId && c.UserId == consent.UserId && c.Channel == consent.Channel && c.Category == consent.Category,
            cancellationToken);

        if (existing is null)
            _context.Consents.Add(consent);
        else
            existing.Set(consent.OptIn, consent.UpdatedAtUtc);

        await _context.SaveChangesAsync(cancellationToken);
    }
}
