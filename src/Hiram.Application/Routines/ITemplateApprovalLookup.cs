using Hiram.Domain.Notifications;

namespace Hiram.Application.Routines;

public interface ITemplateApprovalLookup
{
    Task<TemplateApproval> ForAsync(Guid tenantId, string templateName, NotificationChannel channel, CancellationToken cancellationToken);
}
