using Hiram.Application.Delivery;

namespace Hiram.Infrastructure.Messaging;

// What a channel resolved for a single notification: the provider that answers for this tenant and the
// call that reaches it. Subclasses carry whatever their adapter needs, so the orchestration around the
// send stays identical for every channel.
public abstract class ChannelSend
{
    protected ChannelSend(string provider, string canonicalPayload)
    {
        Provider = provider;
        CanonicalPayload = canonicalPayload;
    }

    // Matches the provider column in delivery_attempts, so an attempt says which adapter was called.
    public string Provider { get; }

    // Hashed in shadow mode, which lets a tenant compare what would have gone out without persisting it.
    public string CanonicalPayload { get; }

    public abstract Task<SendOutcome> SendAsync(CancellationToken cancellationToken);
}
