using Hiram.Domain.Notifications;

namespace Hiram.Domain.Templates;

public sealed class Template
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public NotificationChannel Channel { get; private set; }
    public string Name { get; private set; }
    public string Subject { get; private set; }
    public string Body { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    // Parameterless ctor exists so EF Core can materialize rows without re-running creation invariants.
    private Template()
    {
        Name = null!;
        Subject = null!;
        Body = null!;
    }

    public Template(
        Guid id,
        Guid tenantId,
        NotificationChannel channel,
        string name,
        string subject,
        string body,
        DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Template id is required.", nameof(id));
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        if (!Enum.IsDefined(channel))
            throw new ArgumentOutOfRangeException(nameof(channel), channel, "Unknown notification channel.");
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(subject))
            throw new ArgumentException("Subject is required.", nameof(subject));
        if (string.IsNullOrWhiteSpace(body))
            throw new ArgumentException("Body is required.", nameof(body));

        Id = id;
        TenantId = tenantId;
        Channel = channel;
        Name = name;
        Subject = subject;
        Body = body;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    public void UpdateContent(string subject, string body, DateTimeOffset updatedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(subject))
            throw new ArgumentException("Subject is required.", nameof(subject));
        if (string.IsNullOrWhiteSpace(body))
            throw new ArgumentException("Body is required.", nameof(body));

        Subject = subject;
        Body = body;
        UpdatedAtUtc = updatedAtUtc;
    }
}
