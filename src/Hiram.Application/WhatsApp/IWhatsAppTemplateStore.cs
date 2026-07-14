using Hiram.Domain.WhatsApp;

namespace Hiram.Application.WhatsApp;

public interface IWhatsAppTemplateStore
{
    Task AddAsync(WhatsAppTemplate template, CancellationToken cancellationToken);

    Task<WhatsAppTemplate?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);

    Task<WhatsAppTemplate?> FindByNameAsync(Guid tenantId, string name, CancellationToken cancellationToken);

    Task<IReadOnlyList<WhatsAppTemplate>> ListAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);
}
