using Hiram.Application.Delivery;

namespace Hiram.Infrastructure.Messaging;

// A destination that does not resolve for this tenant, such as a push subscription id that no longer
// exists. Retrying cannot change the answer, so the processor records the permanent failure once and
// skips the retry pipeline entirely.
public sealed class UnresolvedSend : ChannelSend
{
    private readonly string _reason;

    public UnresolvedSend(string provider, string reason, string canonicalPayload)
        : base(provider, canonicalPayload)
    {
        _reason = reason;
    }

    public override Task<SendOutcome> SendAsync(CancellationToken cancellationToken) =>
        Task.FromResult<SendOutcome>(new SendOutcome.PermanentFailure(_reason));
}
