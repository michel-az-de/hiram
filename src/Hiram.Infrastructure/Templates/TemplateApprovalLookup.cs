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

        // The explicit approval flag arrives in step 1.3 (template extended with approval/version/checksum).
        // Until then an existing template counts as approved.
        return template is null
            ? new TemplateApproval(Exists: false, Approved: false)
            : new TemplateApproval(Exists: true, Approved: true);
    }
}
