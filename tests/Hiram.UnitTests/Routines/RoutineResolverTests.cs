using Hiram.Application.Routines;
using Hiram.Domain.Notifications;
using Hiram.Domain.Routines;

namespace Hiram.UnitTests.Routines;

public class RoutineResolverTests
{
    private static readonly Guid Tenant = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private sealed class FakeCatalog(IReadOnlyList<Routine> routines) : IRoutineCatalog
    {
        public Task<IReadOnlyList<Routine>> MatchAsync(Guid tenantId, string eventType, CancellationToken cancellationToken) =>
            Task.FromResult(routines);
    }

    private sealed class FakeTemplates(bool exists, bool approved) : ITemplateApprovalLookup
    {
        public Task<TemplateApproval> ForAsync(Guid tenantId, string templateName, NotificationChannel channel, CancellationToken cancellationToken) =>
            Task.FromResult(new TemplateApproval(exists, approved));
    }

    private static Routine Routine(string eventType, string template, params NotificationChannel[] channels) =>
        new(Guid.NewGuid(), Tenant, eventType, template, channels, NotificationCategory.Transactional, active: true);

    [Fact]
    public async Task Routine_MatchesAllActive()
    {
        var routines = new[]
        {
            Routine("produto_vencendo", "t1", NotificationChannel.Email),
            Routine("produto_vencendo", "t2", NotificationChannel.Email)
        };
        var resolver = new RoutineResolver(new FakeCatalog(routines), new FakeTemplates(exists: true, approved: true));

        var decision = await resolver.ResolveAsync(Tenant, "produto_vencendo", CancellationToken.None);

        Assert.False(decision.NoRoute);
        Assert.Equal(2, decision.Fanout.Count);
        Assert.Empty(decision.Suppressed);
    }

    [Fact]
    public async Task NoRoutine_RecordsNoRoute_NoSend()
    {
        var resolver = new RoutineResolver(new FakeCatalog([]), new FakeTemplates(exists: true, approved: true));

        var decision = await resolver.ResolveAsync(Tenant, "unmapped_event", CancellationToken.None);

        Assert.True(decision.NoRoute);
        Assert.Empty(decision.Fanout);
    }

    [Fact]
    public async Task UnapprovedTemplate_Suppressed_WithReason()
    {
        var routines = new[] { Routine("produto_vencendo", "t1", NotificationChannel.Email) };
        var resolver = new RoutineResolver(new FakeCatalog(routines), new FakeTemplates(exists: true, approved: false));

        var decision = await resolver.ResolveAsync(Tenant, "produto_vencendo", CancellationToken.None);

        Assert.Empty(decision.Fanout);
        var item = Assert.Single(decision.Suppressed);
        Assert.Contains("not approved", item.Reason);
    }
}
