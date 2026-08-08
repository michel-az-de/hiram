using Hiram.Application.Templates;
using Hiram.Domain.Notifications;
using Hiram.Domain.Templates;
using Hiram.Infrastructure.Templates;

namespace Hiram.IntegrationTests.Templates;

// Docker-free: the approval lookup is a thin read over the template store, faked here.
public class TemplateApprovalLookupTests
{
    private static readonly Guid Tenant = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private sealed class FakeTemplateStore(Template? result) : ITemplateStore
    {
        public Task<Template?> FindByNameAsync(Guid tenantId, NotificationChannel channel, string name, CancellationToken cancellationToken) =>
            Task.FromResult(result);

        public Task AddAsync(Template template, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<Template?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken) => Task.FromResult<Template?>(null);
        public Task<IReadOnlyList<Template>> ListAsync(Guid tenantId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Template>>([]);
        public Task<bool> UpdateAsync(Guid tenantId, Guid id, string? subject, string body, DateTimeOffset updatedAtUtc, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<bool> ApproveAsync(Guid tenantId, Guid id, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<bool> DeleteAsync(Guid tenantId, Guid id, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private static Template Template(bool approved)
    {
        var template = new Template(Guid.NewGuid(), Tenant, NotificationChannel.Email, "t1", "subject", "body", DateTimeOffset.UtcNow);
        if (approved)
            template.Approve();
        return template;
    }

    [Fact]
    public async Task ForAsync_ReturnsNotExists_WhenTemplateMissing()
    {
        var lookup = new TemplateApprovalLookup(new FakeTemplateStore(null));

        var approval = await lookup.ForAsync(Tenant, "t1", NotificationChannel.Email, CancellationToken.None);

        Assert.False(approval.Exists);
        Assert.False(approval.Approved);
    }

    [Fact]
    public async Task ForAsync_ReturnsExistsNotApproved_WhenTemplateNotApproved()
    {
        var lookup = new TemplateApprovalLookup(new FakeTemplateStore(Template(approved: false)));

        var approval = await lookup.ForAsync(Tenant, "t1", NotificationChannel.Email, CancellationToken.None);

        Assert.True(approval.Exists);
        Assert.False(approval.Approved);
    }

    [Fact]
    public async Task ForAsync_ReturnsApproved_WhenTemplateApproved()
    {
        var lookup = new TemplateApprovalLookup(new FakeTemplateStore(Template(approved: true)));

        var approval = await lookup.ForAsync(Tenant, "t1", NotificationChannel.Email, CancellationToken.None);

        Assert.True(approval.Exists);
        Assert.True(approval.Approved);
    }
}
