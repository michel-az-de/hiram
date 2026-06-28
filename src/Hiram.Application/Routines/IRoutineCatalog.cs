using Hiram.Domain.Routines;

namespace Hiram.Application.Routines;

public interface IRoutineCatalog
{
    // Active routines for the event, tenant specific plus global. The engine fires all of them.
    Task<IReadOnlyList<Routine>> MatchAsync(Guid tenantId, string eventType, CancellationToken cancellationToken);
}
