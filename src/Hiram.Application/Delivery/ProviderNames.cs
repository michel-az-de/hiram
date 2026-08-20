namespace Hiram.Application.Delivery;

// The stable identifier of each adapter. It is the value stored in the provider column of
// tenant_provider_configs, the key the resolvers match on, and the name of the adapter's own HTTP client,
// so the three cannot drift apart into a config that resolves an adapter pointed at another host.
public static class ProviderNames
{
    public const string Smtp = "smtp";
    public const string Resend = "resend";
    public const string TwilioEmail = "twilio-email";
    public const string TwilioSms = "twilio-sms";
    public const string TwilioWhatsApp = "twilio-whatsapp";

    // Meta's own Cloud API, the second implementation behind IWhatsAppProvider (ADR-030). It sits next to
    // twilio-whatsapp rather than replacing it, and which one a tenant uses is a row in
    // tenant_provider_configs.
    public const string MetaWhatsApp = "meta-whatsapp";
}
