namespace Hiram.Domain.Metering;

public sealed class CreditLedgerEntry
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }

    // Null for entries that are not tied to a notification, like a credit top up.
    public Guid? NotificationId { get; private set; }

    // Signed: a debit is negative, a credit is positive. The balance is the sum of amounts.
    public long Amount { get; private set; }
    public string Reason { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    // Parameterless ctor exists so EF Core can materialize rows without re-running creation invariants.
    private CreditLedgerEntry()
    {
        Reason = null!;
    }

    public CreditLedgerEntry(Guid id, Guid tenantId, Guid? notificationId, long amount, string reason, DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Ledger entry id is required.", nameof(id));
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        if (amount == 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Ledger amount cannot be zero.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Reason is required.", nameof(reason));

        Id = id;
        TenantId = tenantId;
        NotificationId = notificationId;
        Amount = amount;
        Reason = reason;
        CreatedAtUtc = createdAtUtc;
    }

    public static CreditLedgerEntry Debit(Guid tenantId, Guid notificationId, long cost, string reason, DateTimeOffset createdAtUtc)
    {
        if (cost <= 0)
            throw new ArgumentOutOfRangeException(nameof(cost), "A debit cost must be positive.");

        return new CreditLedgerEntry(Guid.NewGuid(), tenantId, notificationId, -cost, reason, createdAtUtc);
    }
}
