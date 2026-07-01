using Hiram.Application.Abstractions;
using Hiram.Application.Messaging;
using Hiram.Domain.Messaging;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hiram.Infrastructure.Persistence;

public sealed class MessageClaimStore : IMessageClaimStore
{
    private const string ClaimIndex = "ux_message_claims_tenant_key";

    private readonly HiramDbContext _context;
    private readonly IClock _clock;

    public MessageClaimStore(HiramDbContext context, IClock clock)
    {
        _context = context;
        _clock = clock;
    }

    public async Task<bool> TryClaimAsync(Guid tenantId, string messageKey, CancellationToken cancellationToken)
    {
        _context.MessageClaims.Add(new MessageClaim(Guid.NewGuid(), tenantId, messageKey, _clock.UtcNow));

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (IsClaimConflict(ex))
        {
            // Already claimed. Detach the failed insert so the context stays usable for the caller.
            foreach (var entry in _context.ChangeTracker.Entries<MessageClaim>().Where(e => e.State == EntityState.Added))
                entry.State = EntityState.Detached;
            return false;
        }
    }

    private static bool IsClaimConflict(DbUpdateException ex) =>
        ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: ClaimIndex
        };
}
