using Hiram.Application.Delivery;

namespace Hiram.Infrastructure.Delivery;

// What a Meta error code means for delivery, decided by the code and not by the status range it arrived in.
// The range gets these wrong in both directions: 131000 is an unknown server-side failure that arrives as a
// 500 and deserves another attempt, while 131047 and every template error arrive as 400 and no retry will
// ever fix them. A code the policy does not know returns null and falls back to the range, so an unmapped
// code is generic rather than mislabelled. Same shape as TwilioErrorPolicy, and deliberately so.
internal static class MetaErrorPolicy
{
    public static SendOutcome? For(int code, string describe)
    {
        return code switch
        {
            // Rate limits, on the app, on the business account and on Cloud API throughput. All pass.
            4 or 80007 or 130429 => new SendOutcome.TransientFailure(describe),

            // Sending is paused while quality recovers. Meta lifts this on its own.
            131048 => new SendOutcome.TransientFailure(describe),

            // Too many messages to the same recipient in a short window.
            131056 => new SendOutcome.TransientFailure(describe),

            // Meta's own unknown failure. It is the one code that genuinely means "try again".
            131000 => new SendOutcome.TransientFailure(describe),

            // The 24h window closed, so free text is refused and only a template goes out. An operator
            // fixes this by sending a template, never by retrying the same message.
            131047 => new SendOutcome.PermanentFailure(describe, DeliveryFailureKind.Configuration),

            // Template problems, all of them settled on Meta's side before the send: missing or unapproved
            // in that language, wrong parameter count, wrong parameter format, content against policy, or
            // paused for low quality.
            132001 or 132000 or 132012 or 132007 or 132015 =>
                new SendOutcome.PermanentFailure(describe, DeliveryFailureKind.Configuration),

            // The business account cannot send: no working payment method, phone number never registered,
            // expired token, or the account restricted for a policy violation. Every one of them needs a
            // person in a console, not another attempt.
            131042 or 133010 or 190 or 368 or 131031 =>
                new SendOutcome.PermanentFailure(describe, DeliveryFailureKind.Configuration),

            // The recipient is not on WhatsApp, or the client is too old to receive this. The contact is
            // wrong at the source, not in this message.
            131026 => new SendOutcome.PermanentFailure(describe, DeliveryFailureKind.InvalidDestination),

            _ => null
        };
    }
}
