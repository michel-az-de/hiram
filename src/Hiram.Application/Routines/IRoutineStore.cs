using Hiram.Domain.Routines;

namespace Hiram.Application.Routines;

public interface IRoutineStore
{
    Task AddAsync(Routine routine, CancellationToken cancellationToken);

    // Returns the active routine for (tenant, event type) so provisioning stays idempotent.
    Task<Routine?> FindActiveAsync(Guid? tenantId, string eventType, CancellationToken cancellationToken);
}
