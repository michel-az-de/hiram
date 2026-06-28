using Hiram.Application.Templates;
using Hiram.Domain.Notifications;
using Hiram.Domain.Templates;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hiram.Infrastructure.Persistence;

public sealed class TemplateStore : ITemplateStore
{
    private const string NameIndex = "ux_templates_tenant_channel_name";

    private readonly HiramDbContext _context;

    public TemplateStore(HiramDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(Template template, CancellationToken cancellationToken)
    {
        _context.Templates.Add(template);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsDuplicateName(ex))
        {
            throw new DuplicateTemplateNameException();
        }
    }

    public Task<Template?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken) =>
        _context.Templates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, cancellationToken);

    public Task<Template?> FindByNameAsync(Guid tenantId, NotificationChannel channel, string name, CancellationToken cancellationToken) =>
        _context.Templates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Channel == channel && x.Name == name, cancellationToken);

    public async Task<IReadOnlyList<Template>> ListAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await _context.Templates
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .OrderBy(x => x.Channel)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

    public async Task<bool> UpdateAsync(Guid tenantId, Guid id, string subject, string body, DateTimeOffset updatedAtUtc, CancellationToken cancellationToken)
    {
        var template = await _context.Templates
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, cancellationToken);
        if (template is null)
            return false;

        template.UpdateContent(subject, body, updatedAtUtc);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ApproveAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        var template = await _context.Templates
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, cancellationToken);
        if (template is null)
            return false;

        template.Approve();
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        var deleted = await _context.Templates
            .Where(x => x.TenantId == tenantId && x.Id == id)
            .ExecuteDeleteAsync(cancellationToken);
        return deleted > 0;
    }

    private static bool IsDuplicateName(DbUpdateException ex) =>
        ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: NameIndex
        };
}
