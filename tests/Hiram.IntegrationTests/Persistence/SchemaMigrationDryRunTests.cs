using System.Data;
using Hiram.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hiram.IntegrationTests.Persistence;

// No container: the idempotent script is generated offline from the migrations, so a dry run must
// never open a connection. That is the whole point of --dry-run before a production migration.
public class SchemaMigrationDryRunTests
{
    [Fact]
    public void Migrate_DryRun_WritesNothing()
    {
        var options = new DbContextOptionsBuilder<HiramDbContext>()
            .UseNpgsql("Host=localhost;Port=1;Database=hiram_dryrun;Username=u;Password=p")
            .Options;
        using var context = new HiramDbContext(options);

        var script = HiramSchema.GenerateScript(context);

        Assert.False(string.IsNullOrWhiteSpace(script));
        Assert.Contains("CREATE TABLE", script, StringComparison.OrdinalIgnoreCase);
        // Idempotent script guards every migration against the history table so re-running is safe.
        Assert.Contains("__EFMigrationsHistory", script);
        // Writes nothing: generating the script never opened the database connection.
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }
}
