using Hiram.Domain.Notifications;
using Hiram.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Testcontainers.PostgreSql;

namespace Hiram.IntegrationTests.Persistence;

public class LegacyStatusMigrationTests : IAsyncLifetime
{
    private static readonly Guid DevTenant = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();

    // Unlike the other suites, this one starts the container without migrating so the test can step
    // through migrations and observe the published to sent conversion against a real legacy row.
    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private HiramDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<HiramDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new HiramDbContext(options);
    }

    [Fact]
    public async Task Migration_ConvertsLegacyPublishedStatusToSent()
    {
        await using var context = NewContext();
        var migrator = context.Database.GetService<IMigrator>();

        await migrator.MigrateAsync("AddOutboxTraceContext");
        await context.Database.ExecuteSqlRawAsync(
            "INSERT INTO notifications.notification_requests " +
            "(id, tenant_id, channel, recipient, subject, body, status, created_at_utc) " +
            "VALUES ({0}, {1}, 'Email', 'ops@example.com', 'hello', 'f0', 'Published', {2})",
            Guid.NewGuid(), DevTenant, DateTimeOffset.UtcNow);

        await migrator.MigrateAsync();

        var status = await context.NotificationRequests.Select(x => x.Status).SingleAsync();
        Assert.Equal(NotificationStatus.Sent, status);
    }
}
