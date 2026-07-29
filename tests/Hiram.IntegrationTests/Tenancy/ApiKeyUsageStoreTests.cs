using Hiram.Domain.Tenants;
using Hiram.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Hiram.IntegrationTests.Tenancy;

public sealed class ApiKeyUsageStoreTests : IAsyncLifetime
{
    private static readonly Guid DevTenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset InitialUsage = new(2026, 7, 29, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();
    private DbContextOptions<HiramDbContext>? _options;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _options = new DbContextOptionsBuilder<HiramDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        await using var context = NewContext();
        await HiramSchema.ApplyAsync(context);
    }

    public async Task DisposeAsync() =>
        await _postgres.DisposeAsync();

    [Fact]
    public async Task RecordUsage_UpdatesAtMostOnceWithinFiveMinuteWindow()
    {
        var apiKeyId = await AddApiKey();

        await RecordUsage(apiKeyId, InitialUsage);
        await RecordUsage(apiKeyId, InitialUsage.AddMinutes(1));

        Assert.Equal(InitialUsage, await ReadLastUsedAt(apiKeyId));
    }

    [Fact]
    public async Task RecordUsage_UpdatesAgainAfterFiveMinuteWindow()
    {
        var apiKeyId = await AddApiKey();
        var nextWindow = InitialUsage.AddMinutes(6);

        await RecordUsage(apiKeyId, InitialUsage);
        await RecordUsage(apiKeyId, nextWindow);

        Assert.Equal(nextWindow, await ReadLastUsedAt(apiKeyId));
    }

    private async Task<Guid> AddApiKey()
    {
        var id = Guid.NewGuid();
        await using var context = NewContext();
        context.ApiKeys.Add(new ApiKey(
            id,
            DevTenantId,
            "integration",
            Guid.NewGuid().ToString("N").PadRight(64, '0'),
            "hk_live_test",
            InitialUsage.AddDays(-1)));
        await context.SaveChangesAsync();
        return id;
    }

    private async Task RecordUsage(Guid apiKeyId, DateTimeOffset whenUtc)
    {
        await using var context = NewContext();
        await new ApiKeyStore(context).RecordUsageAsync(apiKeyId, whenUtc, CancellationToken.None);
    }

    private async Task<DateTimeOffset?> ReadLastUsedAt(Guid apiKeyId)
    {
        await using var context = NewContext();
        return await context.ApiKeys
            .Where(key => key.Id == apiKeyId)
            .Select(key => key.LastUsedAtUtc)
            .SingleAsync();
    }

    private HiramDbContext NewContext() => new(_options!);
}
