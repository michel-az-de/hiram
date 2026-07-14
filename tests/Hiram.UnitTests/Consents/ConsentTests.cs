using Hiram.Application.Consents;
using Hiram.Domain.Consents;
using Hiram.Domain.Notifications;
using Hiram.Domain.Routines;

namespace Hiram.UnitTests.Consents;

public class ConsentTests
{
    private static readonly Guid Tenant = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid User = Guid.Parse("00000000-0000-0000-0000-0000000000aa");

    private sealed class FakeConsentStore : IConsentStore
    {
        public Consent? Current { get; set; }
        public Consent? Upserted { get; private set; }

        public Task<Consent?> GetAsync(Guid tenantId, Guid userId, NotificationChannel channel, NotificationCategory category, CancellationToken cancellationToken) =>
            Task.FromResult(Current);

        public Task UpsertAsync(Consent consent, CancellationToken cancellationToken)
        {
            Upserted = consent;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Consent_OptOut_FiltersChannel()
    {
        var store = new FakeConsentStore
        {
            Current = new Consent(Guid.NewGuid(), Tenant, User, NotificationChannel.Email, NotificationCategory.Operational, optIn: false, DateTimeOffset.UtcNow)
        };
        var policy = new ConsentPolicy(store);

        var allowed = await policy.IsAllowedAsync(Tenant, User, NotificationChannel.Email, NotificationCategory.Operational, CancellationToken.None);

        Assert.False(allowed);
    }

    [Theory]
    [InlineData(NotificationCategory.Transactional)]
    [InlineData(NotificationCategory.Operational)]
    [InlineData(NotificationCategory.Marketing)]
    public async Task WhatsApp_WithoutConsentRecord_IsDeniedInEveryCategory(NotificationCategory category)
    {
        var policy = new ConsentPolicy(new FakeConsentStore { Current = null });

        var allowed = await policy.IsAllowedAsync(Tenant, User, NotificationChannel.WhatsApp, category, CancellationToken.None);

        Assert.False(allowed);
    }

    [Fact]
    public async Task WhatsApp_WithOptInRecord_IsAllowed()
    {
        var store = new FakeConsentStore
        {
            Current = new Consent(Guid.NewGuid(), Tenant, User, NotificationChannel.WhatsApp, NotificationCategory.Transactional, optIn: true, DateTimeOffset.UtcNow)
        };
        var policy = new ConsentPolicy(store);

        var allowed = await policy.IsAllowedAsync(Tenant, User, NotificationChannel.WhatsApp, NotificationCategory.Transactional, CancellationToken.None);

        Assert.True(allowed);
    }

    // Regression guard: the whatsapp deny-by-default must not change email's legitimate-interest default.
    [Theory]
    [InlineData(NotificationCategory.Transactional, true)]
    [InlineData(NotificationCategory.Operational, true)]
    [InlineData(NotificationCategory.Marketing, false)]
    public async Task Email_WithoutConsentRecord_KeepsLegitimateInterestDefault(NotificationCategory category, bool expected)
    {
        var policy = new ConsentPolicy(new FakeConsentStore { Current = null });

        var allowed = await policy.IsAllowedAsync(Tenant, User, NotificationChannel.Email, category, CancellationToken.None);

        Assert.Equal(expected, allowed);
    }

    // ADR-024: an event without a RecipientUserId cannot be looked up, so email falls open for transactional
    // and operational and closed for marketing; WhatsApp always denies.
    [Theory]
    [InlineData(NotificationChannel.Email, NotificationCategory.Transactional, true)]
    [InlineData(NotificationChannel.Email, NotificationCategory.Operational, true)]
    [InlineData(NotificationChannel.Email, NotificationCategory.Marketing, false)]
    [InlineData(NotificationChannel.WhatsApp, NotificationCategory.Transactional, false)]
    public async Task WithoutUserId_FallsOpenForLegitimateInterest_ClosedForMarketing(
        NotificationChannel channel, NotificationCategory category, bool expected)
    {
        var policy = new ConsentPolicy(new FakeConsentStore { Current = null });

        var allowed = await policy.IsAllowedAsync(Tenant, userId: null, channel, category, CancellationToken.None);

        Assert.Equal(expected, allowed);
    }

    [Fact]
    public async Task ConsentDualWrite_ReconcilesDrift()
    {
        // Hiram has no record; the EasyStok snapshot is an opt-out. Reconciling must converge Hiram to it.
        var store = new FakeConsentStore { Current = null };
        var reconciler = new ConsentReconciler(store);
        var local = new ConsentSnapshot(User, NotificationChannel.Email, NotificationCategory.Marketing, OptIn: false, DateTimeOffset.UtcNow);

        var reconciled = await reconciler.ReconcileAsync(Tenant, local, CancellationToken.None);

        Assert.True(reconciled);
        Assert.NotNull(store.Upserted);
        Assert.False(store.Upserted!.OptIn);
        Assert.Equal(User, store.Upserted.UserId);
    }
}
