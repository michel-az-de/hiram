using System.Text.Json;
using Hiram.Domain.Notifications;
using Hiram.Domain.Tenants;
using Hiram.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Hiram.IntegrationTests.Persistence;

public class TenantProviderConfigStoreTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var context = NewContext();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private HiramDbContext NewContext() =>
        new(new DbContextOptionsBuilder<HiramDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);

    [Fact]
    public async Task FindAsync_ReturnsConfig_ForTenantAndChannel()
    {
        var tenantId = Guid.NewGuid();
        await using (var seed = NewContext())
        {
            seed.TenantProviderConfigs.Add(new TenantProviderConfig(
                tenantId, NotificationChannel.Email, "resend", "{\"from\":\"hi@easystok.com\"}", "protected:secret", DateTimeOffset.UtcNow));
            await seed.SaveChangesAsync();
        }

        await using var context = NewContext();
        var store = new TenantProviderConfigStore(context);

        var config = await store.FindAsync(tenantId, NotificationChannel.Email, CancellationToken.None);

        Assert.NotNull(config);
        Assert.Equal("resend", config!.Provider);
        Assert.Equal("protected:secret", config.SecretProtected);
        // Compare the settings by content: Postgres reformats jsonb on the round trip, so the raw string differs.
        var settings = JsonSerializer.Deserialize<Dictionary<string, string>>(config.Settings)!;
        Assert.Equal("hi@easystok.com", settings["from"]);
    }

    [Fact]
    public async Task FindAsync_ReturnsNull_WhenTenantHasNoConfig()
    {
        await using var context = NewContext();
        var store = new TenantProviderConfigStore(context);

        var config = await store.FindAsync(Guid.NewGuid(), NotificationChannel.Email, CancellationToken.None);

        Assert.Null(config);
    }

    [Fact]
    public async Task FindAsync_ReturnsConfig_ForWhatsAppChannel()
    {
        var tenantId = Guid.NewGuid();
        await using (var seed = NewContext())
        {
            seed.TenantProviderConfigs.Add(new TenantProviderConfig(
                tenantId, NotificationChannel.WhatsApp, "cloud-api",
                "{\"phoneNumberId\":\"123456\",\"wabaId\":\"789\"}", "protected:token", DateTimeOffset.UtcNow));
            await seed.SaveChangesAsync();
        }

        await using var context = NewContext();
        var store = new TenantProviderConfigStore(context);

        var config = await store.FindAsync(tenantId, NotificationChannel.WhatsApp, CancellationToken.None);

        Assert.NotNull(config);
        Assert.Equal(NotificationChannel.WhatsApp, config!.Channel);
        Assert.Equal("cloud-api", config.Provider);
        Assert.Equal("protected:token", config.SecretProtected);
        var settings = JsonSerializer.Deserialize<Dictionary<string, string>>(config.Settings)!;
        Assert.Equal("123456", settings["phoneNumberId"]);
    }
}
