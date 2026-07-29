using Hiram.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Testcontainers.PostgreSql;

namespace Hiram.IntegrationTests.Persistence;

public sealed class OutboxLeaseMigrationTests : IAsyncLifetime
{
    private const string PreviousMigration = "20260714024927_AddDeliveryAttemptProviderMessageId";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();
    private DbContextOptions<HiramDbContext>? _options;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _options = new DbContextOptionsBuilder<HiramDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
    }

    public async Task DisposeAsync() =>
        await _postgres.DisposeAsync();

    [Fact]
    public async Task Migration_BackfillsAvailableAtFromDispatchOrCreationTime()
    {
        var createdAt = new DateTimeOffset(2026, 7, 29, 12, 0, 0, TimeSpan.Zero);
        var dispatchAt = createdAt.AddHours(2);
        var directId = Guid.NewGuid();
        var deferredId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var payload = "{}";

        await using var context = new HiramDbContext(_options!);
        var migrator = context.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO notifications.outbox_messages
                (id, tenant_id, type, payload, created_at_utc, processed_at_utc, trace_parent, dispatch_at)
            VALUES
                ({directId}, {tenantId}, 'email', {payload}::jsonb, {createdAt}, NULL, NULL, NULL),
                ({deferredId}, {tenantId}, 'email', {payload}::jsonb, {createdAt}, NULL, NULL, {dispatchAt})
            """);

        await migrator.MigrateAsync();
        context.ChangeTracker.Clear();

        var direct = await context.OutboxMessages.AsNoTracking().SingleAsync(message => message.Id == directId);
        var deferred = await context.OutboxMessages.AsNoTracking().SingleAsync(message => message.Id == deferredId);
        Assert.Equal(createdAt, direct.AvailableAt);
        Assert.Equal(dispatchAt, deferred.AvailableAt);
        Assert.Equal(0, direct.AttemptCount);
        Assert.Null(direct.LeaseUntil);
        Assert.Null(direct.LastError);
    }
}
