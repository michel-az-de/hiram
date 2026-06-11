using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Hiram.Infrastructure.Persistence;

// Used only by the EF Core tooling to build the model when generating migrations.
internal sealed class HiramDbContextFactory : IDesignTimeDbContextFactory<HiramDbContext>
{
    public HiramDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<HiramDbContext>()
            .UseNpgsql("Host=localhost;Port=5433;Database=hiram;Username=hiram;Password=hiram")
            .Options;

        return new HiramDbContext(options);
    }
}
