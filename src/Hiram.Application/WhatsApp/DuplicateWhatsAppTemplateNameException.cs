namespace Hiram.Application.WhatsApp;

public sealed class DuplicateWhatsAppTemplateNameException : Exception
{
    public DuplicateWhatsAppTemplateNameException()
        : base("A whatsapp template with the same name already exists for this tenant.")
    {
    }
}
