using Hiram.Domain.Notifications;
using Hiram.Domain.Routines;
using Hiram.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Hiram.IntegrationTests.Persistence;

public class RoutineCatalogTests : IAsyncLifetime
{
    private static readonly Guid Tenant = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherTenant = Guid.Parse("00000000-0000-0000-0000-000000000002");

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var context = NewContext();
        await context.Database.MigrateAsync();

        context.Routines.AddRange(
            Routine(Tenant, "produto_vencendo", active: true),
            Routine(null, "produto_vencendo", active: true),
            Routine(Tenant, "produto_vencendo", active: false),
            Routine(Tenant, "outro_evento", active: true),
            Routine(OtherTenant, "produto_vencendo", active: true));
        await context.SaveChangesAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private HiramDbContext NewContext() =>
        new(new DbContextOptionsBuilder<HiramDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);

    private static Routine Routine(Guid? tenantId, string eventType, bool active) =>
        new(Guid.NewGuid(), tenantId, eventType, "template", new[] { NotificationChannel.Email }, NotificationCategory.Transactional, active);

    [Fact]
    public async Task Match_ReturnsActiveTenantAndGlobalRoutines_ForTheEvent()
    {
        await using var context = NewContext();
        var catalog = new RoutineCatalog(context);

        var matched = await catalog.MatchAsync(Tenant, "produto_vencendo", CancellationToken.None);

        // Tenant-specific active plus global active, but not inactive, other event or other tenant.
        Assert.Equal(2, matched.Count);
        Assert.All(matched, r => Assert.True(r.TenantId == Tenant || r.TenantId is null));
        Assert.All(matched, r => Assert.Equal("produto_vencendo", r.EventType));
        Assert.Contains(matched, r => r.Channels.Contains(NotificationChannel.Email));
    }
}
