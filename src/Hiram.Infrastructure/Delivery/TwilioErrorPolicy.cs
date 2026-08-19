using Hiram.Application.Delivery;

namespace Hiram.Infrastructure.Delivery;

// What a Twilio error code means for delivery, decided by the code and not by the status range it arrived
// in. The range gets some of these right by accident: 30007 is a 201 with a terminal status, and a range
// rule that read it as retryable would make the Hiram raise its own sender's spam score with every retry.
// A code the policy does not know returns null and falls back to the range, so an unmapped code is
// generic rather than mislabelled.
internal static class TwilioErrorPolicy
{
    public static SendOutcome? For(int code, string describe)
    {
        return code switch
        {
            // The carrier classified the message as spam. Retrying is what makes it worse.
            30007 => new SendOutcome.PermanentFailure(describe, DeliveryFailureKind.Provider),

            // The handset was unreachable, which is a condition that passes.
            30003 => new SendOutcome.TransientFailure(describe),

            // The number does not exist, or is not a phone at all. The contact is wrong at the source.
            30005 or 21211 or 21614 => new SendOutcome.PermanentFailure(describe, DeliveryFailureKind.InvalidDestination),

            // The account is missing something an operator has to turn on: the destination region in geo
            // permissions, or the US campaign registration.
            21408 or 30034 => new SendOutcome.PermanentFailure(describe, DeliveryFailureKind.Configuration),

            // Free text where an approved template is required. This is what a closed WhatsApp session
            // actually answers: measured six times against the sandbox in 2026-08-10 with the window
            // verifiably shut, always 21654 and never the 63016 the documentation predicts (issue #133).
            // Both are mapped because they mean the same thing for delivery and only one was observed.
            21654 or 63016 => new SendOutcome.PermanentFailure(describe, DeliveryFailureKind.Configuration),

            // The recipient replied STOP to the carrier.
            21610 => new SendOutcome.PermanentFailure(describe, DeliveryFailureKind.RecipientOptedOut),

            _ => null
        };
    }
}
