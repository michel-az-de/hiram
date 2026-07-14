using Hiram.Application.WhatsApp;
using Hiram.Domain.WhatsApp;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hiram.Infrastructure.Persistence;

public sealed class WhatsAppTemplateStore : IWhatsAppTemplateStore
{
    private const string NameIndex = "ux_whatsapp_templates_tenant_name";

    private readonly HiramDbContext _context;

    public WhatsAppTemplateStore(HiramDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(WhatsAppTemplate template, CancellationToken cancellationToken)
    {
        _context.WhatsAppTemplates.Add(template);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsDuplicateName(ex))
        {
            throw new DuplicateWhatsAppTemplateNameException();
        }
    }

    public Task<WhatsAppTemplate?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken) =>
        _context.WhatsAppTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, cancellationToken);

    public Task<WhatsAppTemplate?> FindByNameAsync(Guid tenantId, string name, CancellationToken cancellationToken) =>
        _context.WhatsAppTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Name == name, cancellationToken);

    public async Task<IReadOnlyList<WhatsAppTemplate>> ListAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await _context.WhatsAppTemplates
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

    public async Task<bool> DeleteAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        var deleted = await _context.WhatsAppTemplates
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
