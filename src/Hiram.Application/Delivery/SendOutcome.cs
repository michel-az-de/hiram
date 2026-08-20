namespace Hiram.Application.Delivery;

// Explicit result of a send attempt. Each provider adapter classifies its own errors into transient
// (worth retrying) or permanent (fail fast), so the pipeline never inspects provider specific details.
public abstract record SendOutcome
{
    private SendOutcome()
    {
    }

    // ProviderMessageId is the provider's own id for the accepted message, null when the provider returns
    // none such as SMTP. It is the handle a status callback matches on. TrialContent is true when the
    // adapter sent approved content instead of the notification body, which a trial account forces
    // (ADR-028): without it the history would claim to have delivered a text that never left.
    public sealed record Sent(string? ProviderMessageId = null, bool TrialContent = false) : SendOutcome;

    public sealed record TransientFailure(string Reason) : SendOutcome;

    // Kind is what an operator acts on: a misconfigured account and a recipient who opted out both land
    // here, and nothing but the provider's own code tells them apart. Adapters that cannot tell leave the
    // default rather than guessing.
    public sealed record PermanentFailure(string Reason, DeliveryFailureKind Kind = DeliveryFailureKind.Provider)
        : SendOutcome;
}
