using Hiram.Application.Templates;
using Hiram.Domain.Notifications;
using Hiram.Domain.Templates;
using Hiram.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Hiram.IntegrationTests.Persistence;

public class TemplateStoreTests : IAsyncLifetime
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

    private static Template NewTemplate(Guid tenantId, string name) =>
        new(Guid.NewGuid(), tenantId, NotificationChannel.Email, name, "Hi {{ name }}", "Welcome {{ name }}", DateTimeOffset.UtcNow);

    [Fact]
    public async Task Add_And_FindByName_RoundTrip()
    {
        var tenantId = Guid.NewGuid();
        await using (var context = NewContext())
            await new TemplateStore(context).AddAsync(NewTemplate(tenantId, "welcome"), CancellationToken.None);

        await using var read = NewContext();
        var found = await new TemplateStore(read).FindByNameAsync(tenantId, NotificationChannel.Email, "welcome", CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal("Hi {{ name }}", found!.Subject);
    }

    [Fact]
    public async Task Add_Throws_OnDuplicateNamePerChannel()
    {
        var tenantId = Guid.NewGuid();
        await using (var context = NewContext())
            await new TemplateStore(context).AddAsync(NewTemplate(tenantId, "welcome"), CancellationToken.None);

        await using var context2 = NewContext();
        await Assert.ThrowsAsync<DuplicateTemplateNameException>(() =>
            new TemplateStore(context2).AddAsync(NewTemplate(tenantId, "welcome"), CancellationToken.None));
    }

    [Fact]
    public async Task List_IsScopedToTenant()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await using (var context = NewContext())
        {
            var store = new TemplateStore(context);
            await store.AddAsync(NewTemplate(tenantA, "a1"), CancellationToken.None);
            await store.AddAsync(NewTemplate(tenantB, "b1"), CancellationToken.None);
        }

        await using var read = NewContext();
        var list = await new TemplateStore(read).ListAsync(tenantA, CancellationToken.None);

        Assert.Single(list);
        Assert.Equal("a1", list[0].Name);
    }

    [Fact]
    public async Task Update_ChangesContent_AndReturnsFalseWhenMissing()
    {
        var tenantId = Guid.NewGuid();
        var template = NewTemplate(tenantId, "welcome");
        await using (var context = NewContext())
            await new TemplateStore(context).AddAsync(template, CancellationToken.None);

        await using (var context = NewContext())
        {
            var updated = await new TemplateStore(context).UpdateAsync(
                tenantId, template.Id, "New {{ name }}", "Body {{ name }}", DateTimeOffset.UtcNow, CancellationToken.None);
            Assert.True(updated);
        }

        await using var read = NewContext();
        var found = await new TemplateStore(read).GetAsync(tenantId, template.Id, CancellationToken.None);
        Assert.Equal("New {{ name }}", found!.Subject);

        var missing = await new TemplateStore(read).UpdateAsync(
            tenantId, Guid.NewGuid(), "x", "y", DateTimeOffset.UtcNow, CancellationToken.None);
        Assert.False(missing);
    }

    [Fact]
    public async Task Delete_RemovesTemplate_AndReturnsFalseWhenMissing()
    {
        var tenantId = Guid.NewGuid();
        var template = NewTemplate(tenantId, "welcome");
        await using (var context = NewContext())
            await new TemplateStore(context).AddAsync(template, CancellationToken.None);

        await using var context2 = NewContext();
        var store = new TemplateStore(context2);

        Assert.True(await store.DeleteAsync(tenantId, template.Id, CancellationToken.None));
        Assert.False(await store.DeleteAsync(tenantId, template.Id, CancellationToken.None));
        Assert.Null(await store.GetAsync(tenantId, template.Id, CancellationToken.None));
    }
}
