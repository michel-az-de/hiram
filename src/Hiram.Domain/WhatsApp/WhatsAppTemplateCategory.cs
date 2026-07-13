namespace Hiram.Domain.WhatsApp;

// Meta's template categories, which drive conversation pricing. Distinct from Hiram's
// NotificationCategory, which governs consent and legitimate interest.
public enum WhatsAppTemplateCategory
{
    Utility = 1,
    Authentication = 2,
    Marketing = 3
}
