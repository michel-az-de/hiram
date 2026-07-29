using Hiram.Application.Tenancy;
using Hiram.Domain.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Hiram.Infrastructure.Persistence;

public sealed class ApiKeyStore : IApiKeyStore
{
    private static readonly TimeSpan UsageWindow = TimeSpan.FromMinutes(5);

    private readonly HiramDbContext _context;

    public ApiKeyStore(HiramDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(ApiKey apiKey, CancellationToken cancellationToken)
    {
        _context.ApiKeys.Add(apiKey);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public Task<ApiKey?> FindByHashAsync(string keyHash, CancellationToken cancellationToken) =>
        _context.ApiKeys.AsNoTracking().FirstOrDefaultAsync(x => x.KeyHash == keyHash, cancellationToken);

    public async Task<bool> RevokeAsync(Guid apiKeyId, DateTimeOffset whenUtc, CancellationToken cancellationToken)
    {
        var apiKey = await _context.ApiKeys.FirstOrDefaultAsync(x => x.Id == apiKeyId, cancellationToken);
        if (apiKey is null)
            return false;

        apiKey.Revoke(whenUtc);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    // The predicate makes the throttle atomic in Postgres, including concurrent requests, while the
    // targeted update avoids loading the row on the authentication hot path.
    public Task RecordUsageAsync(Guid apiKeyId, DateTimeOffset whenUtc, CancellationToken cancellationToken) =>
        _context.ApiKeys
            .Where(x => x.Id == apiKeyId
                && (x.LastUsedAtUtc == null || x.LastUsedAtUtc <= whenUtc - UsageWindow))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastUsedAtUtc, whenUtc), cancellationToken);
}
