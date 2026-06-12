using Hiram.Application.Abstractions;
using Hiram.Application.Tenancy;

namespace Hiram.Api.Authentication;

public sealed class ApiKeyMiddleware
{
    private const string HeaderName = "X-Api-Key";
    private const string ProtectedPrefix = "/v1";
    private const string AdminPrefix = "/v1/admin";

    private readonly RequestDelegate _next;
    private readonly IProblemDetailsService _problemDetails;

    public ApiKeyMiddleware(RequestDelegate next, IProblemDetailsService problemDetails)
    {
        _next = next;
        _problemDetails = problemDetails;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IApiKeyStore apiKeys,
        IApiKeyUsageThrottle throttle,
        TenantContext tenant,
        IClock clock)
    {
        var path = context.Request.Path;

        // Admin endpoints carry their own X-Admin-Key gate; everything outside /v1 is public (docs, health).
        if (!path.StartsWithSegments(ProtectedPrefix) || path.StartsWithSegments(AdminPrefix))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(HeaderName, out var presented) || string.IsNullOrWhiteSpace(presented))
        {
            await WriteUnauthorized(context, "A valid X-Api-Key header is required.");
            return;
        }

        var hash = ApiKeyHasher.Hash(presented.ToString());
        var apiKey = await apiKeys.FindByHashAsync(hash, context.RequestAborted);
        if (apiKey is null || apiKey.IsRevoked)
        {
            await WriteUnauthorized(context, "The provided API key is invalid or has been revoked.");
            return;
        }

        tenant.Resolve(apiKey.TenantId);

        if (await throttle.ShouldRecordUsageAsync(apiKey.Id, context.RequestAborted))
            await apiKeys.RecordUsageAsync(apiKey.Id, clock.UtcNow, context.RequestAborted);

        await _next(context);
    }

    private async Task WriteUnauthorized(HttpContext context, string detail)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await _problemDetails.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails =
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Unauthorized",
                Detail = detail
            }
        });
    }
}
