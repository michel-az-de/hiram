using Hiram.Application.Webhooks;
using Hiram.Domain.Webhooks;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hiram.Infrastructure.Persistence;

public sealed class WebhookEndpointStore : IWebhookEndpointStore
{
    private const string UrlIndex = "ux_webhook_endpoints_tenant_url";

    private readonly HiramDbContext _context;

    public WebhookEndpointStore(HiramDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(WebhookEndpoint endpoint, CancellationToken cancellationToken)
    {
        _context.WebhookEndpoints.Add(endpoint);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsDuplicateUrl(ex))
        {
            throw new DuplicateWebhookEndpointException();
        }
    }

    public async Task<IReadOnlyList<WebhookEndpoint>> ListAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await _context.WebhookEndpoints
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public Task<bool> HasAnyAsync(Guid tenantId, CancellationToken cancellationToken) =>
        _context.WebhookEndpoints.AnyAsync(x => x.TenantId == tenantId, cancellationToken);

    public async Task<bool> DeleteAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        var deleted = await _context.WebhookEndpoints
            .Where(x => x.TenantId == tenantId && x.Id == id)
            .ExecuteDeleteAsync(cancellationToken);
        return deleted > 0;
    }

    private static bool IsDuplicateUrl(DbUpdateException ex) =>
        ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: UrlIndex
        };
}
