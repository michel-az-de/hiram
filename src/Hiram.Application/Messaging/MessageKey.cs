using System.Security.Cryptography;
using System.Text;
using Hiram.Domain.Notifications;

namespace Hiram.Application.Messaging;

public static class MessageKey
{
    // Deterministic per-message idempotency key, computed from the decision at fan-out and frozen with
    // the message (never recomputed at delivery). Transactional events have no schedule slot, so it
    // collapses to a fixed value, keeping the key stable across redelivery.
    public static string Compute(string eventId, NotificationChannel channel, string recipient, int templateVersion, string? dispatchSlot)
    {
        var slot = string.IsNullOrEmpty(dispatchSlot) ? "_" : dispatchSlot;
        var raw = $"{eventId}|{channel}|{recipient}|{templateVersion}|{slot}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }
}
