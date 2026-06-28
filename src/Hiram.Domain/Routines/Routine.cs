using Hiram.Domain.Notifications;

namespace Hiram.Domain.Routines;

// Maps an event type to what to send: a template and an ordered set of channels (the fallback order),
// scoped to a tenant or global. A cron window and timezone arrive in step 1.7.
public sealed class Routine
{
    public Guid Id { get; private set; }
    public Guid? TenantId { get; private set; }
    public string EventType { get; private set; }
    public string TemplateName { get; private set; }
    public IReadOnlyList<NotificationChannel> Channels { get; private set; }
    public NotificationCategory Category { get; private set; }
    public bool Active { get; private set; }

    private Routine()
    {
        EventType = null!;
        TemplateName = null!;
        Channels = [];
    }

    public Routine(
        Guid id,
        Guid? tenantId,
        string eventType,
        string templateName,
        IReadOnlyList<NotificationChannel> channels,
        NotificationCategory category,
        bool active)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Routine id is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(eventType))
            throw new ArgumentException("Event type is required.", nameof(eventType));
        if (string.IsNullOrWhiteSpace(templateName))
            throw new ArgumentException("Template name is required.", nameof(templateName));
        if (channels is null || channels.Count == 0)
            throw new ArgumentException("At least one channel is required.", nameof(channels));

        Id = id;
        TenantId = tenantId;
        EventType = eventType;
        TemplateName = templateName;
        Channels = channels;
        Category = category;
        Active = active;
    }
}
