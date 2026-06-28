using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Hiram.Infrastructure.Persistence;

public static class HiramSchema
{
    public static string GenerateScript(HiramDbContext context) =>
        context.Database.GetService<IMigrator>()
            .GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);

    public static Task ApplyAsync(HiramDbContext context, CancellationToken cancellationToken = default) =>
        context.Database.MigrateAsync(cancellationToken);
}
