using Hiram.Application.WhatsApp;
using Hiram.Domain.WhatsApp;
using Hiram.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Hiram.IntegrationTests.Persistence;

public class WhatsAppTemplateStoreTests : IAsyncLifetime
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

    private static WhatsAppTemplate NewTemplate(Guid tenantId, string name) =>
        new(Guid.NewGuid(), tenantId, name, "order_shipped", "pt_BR", WhatsAppTemplateCategory.Utility,
            ["customerName", "trackingCode"], DateTimeOffset.UtcNow);

    [Fact]
    public async Task Add_And_FindByName_RoundTripsParametersAndStatus()
    {
        var tenantId = Guid.NewGuid();
        var template = NewTemplate(tenantId, "order-shipped");
        template.RecordMetaStatus(WhatsAppTemplateStatus.Approved, DateTimeOffset.UtcNow);
        await using (var context = NewContext())
            await new WhatsAppTemplateStore(context).AddAsync(template, CancellationToken.None);

        await using var read = NewContext();
        var found = await new WhatsAppTemplateStore(read).FindByNameAsync(tenantId, "order-shipped", CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal("order_shipped", found!.MetaName);
        Assert.Equal("pt_BR", found.Language);
        Assert.Equal(WhatsAppTemplateCategory.Utility, found.Category);
        Assert.Equal(WhatsAppTemplateStatus.Approved, found.Status);
        Assert.Equal(new[] { "customerName", "trackingCode" }, found.Parameters);
        Assert.True(found.IsSendable);
    }

    [Fact]
    public async Task Add_Throws_OnDuplicateNamePerTenant()
    {
        var tenantId = Guid.NewGuid();
        await using (var context = NewContext())
            await new WhatsAppTemplateStore(context).AddAsync(NewTemplate(tenantId, "welcome"), CancellationToken.None);

        await using var context2 = NewContext();
        await Assert.ThrowsAsync<DuplicateWhatsAppTemplateNameException>(() =>
            new WhatsAppTemplateStore(context2).AddAsync(NewTemplate(tenantId, "welcome"), CancellationToken.None));
    }

    [Fact]
    public async Task SameName_IsAllowed_ForDifferentTenants()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using var context = NewContext();
        var store = new WhatsAppTemplateStore(context);
        await store.AddAsync(NewTemplate(tenantA, "welcome"), CancellationToken.None);
        await store.AddAsync(NewTemplate(tenantB, "welcome"), CancellationToken.None);

        Assert.NotNull(await store.FindByNameAsync(tenantA, "welcome", CancellationToken.None));
        Assert.NotNull(await store.FindByNameAsync(tenantB, "welcome", CancellationToken.None));
    }

    [Fact]
    public async Task List_IsScopedToTenant()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await using (var context = NewContext())
        {
            var store = new WhatsAppTemplateStore(context);
            await store.AddAsync(NewTemplate(tenantA, "a1"), CancellationToken.None);
            await store.AddAsync(NewTemplate(tenantB, "b1"), CancellationToken.None);
        }

        await using var read = NewContext();
        var list = await new WhatsAppTemplateStore(read).ListAsync(tenantA, CancellationToken.None);

        Assert.Single(list);
        Assert.Equal("a1", list[0].Name);
    }

    [Fact]
    public async Task Reads_And_Delete_AreIsolatedByTenant()
    {
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        var template = NewTemplate(owner, "welcome");
        await using (var context = NewContext())
            await new WhatsAppTemplateStore(context).AddAsync(template, CancellationToken.None);

        await using var read = NewContext();
        var store = new WhatsAppTemplateStore(read);

        Assert.Null(await store.GetAsync(other, template.Id, CancellationToken.None));
        Assert.Null(await store.FindByNameAsync(other, "welcome", CancellationToken.None));
        Assert.False(await store.DeleteAsync(other, template.Id, CancellationToken.None));

        Assert.NotNull(await store.GetAsync(owner, template.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Delete_RemovesTemplate_AndReturnsFalseWhenMissing()
    {
        var tenantId = Guid.NewGuid();
        var template = NewTemplate(tenantId, "welcome");
        await using (var context = NewContext())
            await new WhatsAppTemplateStore(context).AddAsync(template, CancellationToken.None);

        await using var context2 = NewContext();
        var store = new WhatsAppTemplateStore(context2);

        Assert.True(await store.DeleteAsync(tenantId, template.Id, CancellationToken.None));
        Assert.False(await store.DeleteAsync(tenantId, template.Id, CancellationToken.None));
        Assert.Null(await store.GetAsync(tenantId, template.Id, CancellationToken.None));
    }
}
