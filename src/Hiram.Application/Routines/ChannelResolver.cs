using Hiram.Application.Blocks;
using Hiram.Application.Consents;
using Hiram.Domain.Notifications;
using Hiram.Domain.Routines;

namespace Hiram.Application.Routines;

// Given a routine's fallback order, keeps the channels that pass both the kill-switch and consent, in
// order. This is the per-event channel decision the fan-out uses.
public sealed class ChannelResolver
{
    private readonly ConsentPolicy _consent;
    private readonly BlockGate _blocks;

    public ChannelResolver(ConsentPolicy consent, BlockGate blocks)
    {
        _consent = consent;
        _blocks = blocks;
    }

    public async Task<IReadOnlyList<NotificationChannel>> ResolveAsync(
        Guid tenantId,
        Guid userId,
        NotificationCategory category,
        IReadOnlyList<NotificationChannel> ordered,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var allowed = new List<NotificationChannel>();
        foreach (var channel in ordered)
        {
            if (await _blocks.IsBlockedAsync(tenantId, channel, now, cancellationToken))
                continue;
            if (!await _consent.IsAllowedAsync(tenantId, userId, channel, category, cancellationToken))
                continue;

            allowed.Add(channel);
        }

        return allowed;
    }
}
