using Hiram.Application.Routines;
using Hiram.Application.Templates;
using Hiram.Domain.Notifications;

namespace Hiram.Infrastructure.Templates;

public sealed class TemplateApprovalLookup : ITemplateApprovalLookup
{
    private readonly ITemplateStore _templates;

    public TemplateApprovalLookup(ITemplateStore templates)
    {
        _templates = templates;
    }

    public async Task<TemplateApproval> ForAsync(Guid tenantId, string templateName, NotificationChannel channel, CancellationToken cancellationToken)
    {
        var template = await _templates.FindByNameAsync(tenantId, channel, templateName, cancellationToken);

        return template is null
            ? new TemplateApproval(Exists: false, Approved: false)
            : new TemplateApproval(Exists: true, Approved: template.Approved);
    }
}
