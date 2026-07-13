namespace Hiram.Domain.WhatsApp;

// A Meta-approved HSM template. Unlike the Scriban Template, the body is authored and rendered by Meta;
// Hiram sends only the template name plus positional parameters. Approval belongs to Meta, tracked in
// Status, so the local Approved flag of a Scriban Template does not apply here.
public sealed class WhatsAppTemplate
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }

    // Local name the tenant references when submitting a notification.
    public string Name { get; private set; }

    // Template name as registered in the Meta WhatsApp Manager.
    public string MetaName { get; private set; }

    // Language and locale the Meta template is authored in, e.g. "pt_BR".
    public string Language { get; private set; }

    public WhatsAppTemplateCategory Category { get; private set; }
    public WhatsAppTemplateStatus Status { get; private set; }

    // Ordered data keys mapped to the HSM body parameters {{1}}, {{2}}...; the list index is the position.
    public IReadOnlyList<string> Parameters { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public bool IsSendable => Status == WhatsAppTemplateStatus.Approved;

    // Parameterless ctor exists so EF Core can materialize rows without re-running creation invariants.
    private WhatsAppTemplate()
    {
        Name = null!;
        MetaName = null!;
        Language = null!;
        Parameters = null!;
    }

    public WhatsAppTemplate(
        Guid id,
        Guid tenantId,
        string name,
        string metaName,
        string language,
        WhatsAppTemplateCategory category,
        IReadOnlyList<string> parameters,
        DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Template id is required.", nameof(id));
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(metaName))
            throw new ArgumentException("Meta template name is required.", nameof(metaName));
        if (string.IsNullOrWhiteSpace(language))
            throw new ArgumentException("Language is required.", nameof(language));
        if (!Enum.IsDefined(category))
            throw new ArgumentOutOfRangeException(nameof(category), category, "Unknown template category.");
        ArgumentNullException.ThrowIfNull(parameters);
        if (parameters.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Parameter names cannot be blank.", nameof(parameters));

        Id = id;
        TenantId = tenantId;
        Name = name;
        MetaName = metaName;
        Language = language;
        Category = category;
        Status = WhatsAppTemplateStatus.Pending;
        Parameters = parameters.ToList();
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    // Meta reports approval decisions and later quality-driven changes asynchronously by webhook.
    public void RecordMetaStatus(WhatsAppTemplateStatus status, DateTimeOffset updatedAtUtc)
    {
        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown template status.");

        Status = status;
        UpdatedAtUtc = updatedAtUtc;
    }
}
