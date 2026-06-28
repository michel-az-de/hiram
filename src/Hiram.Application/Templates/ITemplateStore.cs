using Hiram.Domain.Notifications;
using Hiram.Domain.Templates;

namespace Hiram.Application.Templates;

public interface ITemplateStore
{
    Task AddAsync(Template template, CancellationToken cancellationToken);

    Task<Template?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);

    Task<Template?> FindByNameAsync(Guid tenantId, NotificationChannel channel, string name, CancellationToken cancellationToken);

    Task<IReadOnlyList<Template>> ListAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<bool> UpdateAsync(Guid tenantId, Guid id, string subject, string body, DateTimeOffset updatedAtUtc, CancellationToken cancellationToken);

    Task<bool> ApproveAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);
}
