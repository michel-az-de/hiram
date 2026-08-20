namespace Hiram.Application.Delivery;

// No subject: WhatsApp is a single message delivered to a phone number in E.164 format. The recipient is
// the bare number, without the "whatsapp:" prefix the Twilio API expects, because that prefix is a detail
// of one adapter and not something the request, the fan-out or a replay should carry.
//
// The two shapes exist because the providers genuinely disagree on what they accept (ADR-030). Free text
// only leaves inside the 24h window a recipient opens by writing first, and every transactional
// notification is born outside it, where nothing but a template Meta already approved goes out. Modelling
// that as one record with nullable fields would allow a template with a body and no name, so the shape is
// closed here and each adapter states which ones it can send.
public abstract record WhatsAppMessage
{
    private WhatsAppMessage()
    {
    }

    public abstract string Recipient { get; }

    // What identifies this send when the pipeline deduplicates. The shapes cannot share a format: a body
    // and a template that render to the same words are still different messages to the provider.
    public abstract string Canonical { get; }

    public sealed record FreeForm : WhatsAppMessage
    {
        public FreeForm(string recipient, string body)
        {
            Recipient = recipient;
            Body = body;
        }

        public override string Recipient { get; }

        public string Body { get; }

        public override string Canonical => $"{Recipient}\n{Body}";
    }

    // Name and Language identify the template on the provider side, and Parameters fill its placeholders in
    // order. There is no body: Meta renders it, so carrying a local rendering would be a second source of
    // truth for text the recipient never sees.
    public sealed record Template : WhatsAppMessage
    {
        public Template(string recipient, string name, string language, IReadOnlyList<string> parameters)
        {
            Recipient = recipient;
            Name = name;
            Language = language;
            Parameters = parameters;
        }

        public override string Recipient { get; }

        public string Name { get; }

        public string Language { get; }

        public IReadOnlyList<string> Parameters { get; }

        // The count comes before the values on purpose. Joining by a newline alone would let one parameter
        // holding a line break look exactly like two parameters, and the count is what tells them apart.
        public override string Canonical =>
            $"{Recipient}\n{Name}\n{Language}\n{Parameters.Count}\n{string.Join('\n', Parameters)}";
    }
}
