namespace Hiram.Domain.WhatsApp;

// Lifecycle of an HSM template as owned by the Meta WhatsApp Manager. The tenant submits a template,
// Meta reviews it asynchronously, and later may pause or disable it for quality reasons. Only Approved
// is sendable.
public enum WhatsAppTemplateStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Paused = 4,
    Disabled = 5
}
