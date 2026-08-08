namespace Hiram.Application.Delivery;

// No subject: WhatsApp is a single body delivered to a phone number in E.164 format. The recipient is
// the bare number, without the "whatsapp:" prefix the Twilio API expects, because that prefix is a
// detail of one adapter and not something the request, the fan-out or a replay should carry.
public sealed record WhatsAppMessage(string Recipient, string Body);
