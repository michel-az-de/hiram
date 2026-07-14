namespace Hiram.Application.Delivery;

// Explicit result of a send attempt. Each provider adapter classifies its own errors into transient
// (worth retrying) or permanent (fail fast), so the pipeline never inspects provider specific details.
public abstract record SendOutcome
{
    private SendOutcome()
    {
    }

    // ProviderMessageId is the provider's own id for the accepted message (Resend's id, later WhatsApp's
    // wamid), null when the provider returns none such as SMTP. It is the handle a status callback matches on.
    public sealed record Sent(string? ProviderMessageId = null) : SendOutcome;

    public sealed record TransientFailure(string Reason) : SendOutcome;

    public sealed record PermanentFailure(string Reason) : SendOutcome;
}
