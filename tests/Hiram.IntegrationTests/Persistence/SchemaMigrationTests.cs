using Hiram.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Hiram.IntegrationTests.Persistence;

// Starts the container without migrating so the migration itself is the system under test.
public class SchemaMigrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();

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
    public async Task Migrate_OnEmptyDb_CreatesSchema()
    {
        await using var context = NewContext();
        Assert.NotEmpty(await context.Database.GetPendingMigrationsAsync());

        await HiramSchema.ApplyAsync(context);

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.NotEmpty(await context.Database.GetAppliedMigrationsAsync());
        // The schema is usable: a known table exists and answers a query.
        Assert.Equal(0, await context.NotificationRequests.CountAsync());
    }

    [Fact]
    public async Task Migrate_OnCurrentDb_IsNoop()
    {
        await using (var first = NewContext())
            await HiramSchema.ApplyAsync(first);

        await using var context = NewContext();
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());

        // Applying again on an up to date database does nothing and does not throw.
        await HiramSchema.ApplyAsync(context);

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
    }
}
