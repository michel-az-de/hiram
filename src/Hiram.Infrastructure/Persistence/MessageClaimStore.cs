using Hiram.Application.Messaging;
using Hiram.Domain.Messaging;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hiram.Infrastructure.Persistence;

public sealed class MessageClaimStore : IMessageClaimStore
{
    private const string ClaimIndex = "ux_message_claims_tenant_key";

    private readonly HiramDbContext _context;

    public MessageClaimStore(HiramDbContext context)
    {
        _context = context;
    }

    public async Task<bool> TryClaimAsync(Guid tenantId, string messageKey, CancellationToken cancellationToken)
    {
        _context.MessageClaims.Add(new MessageClaim(Guid.NewGuid(), tenantId, messageKey, DateTimeOffset.UtcNow));

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
