using System.Text.Json;
using Hiram.Api.Authentication;
using Hiram.Application.Abstractions;
using Hiram.Application.Delivery;
using Hiram.Application.Tenancy;
using Hiram.Contracts;
using Hiram.Domain.Notifications;
using Hiram.Domain.Tenants;
using Microsoft.Extensions.Logging;

namespace Hiram.Api.Providers;

internal static class ProviderConfigEndpoints
{
    public static IEndpointRouteBuilder MapProviderConfigEndpoints(this IEndpointRouteBuilder app)
    {
        // Tenant self-service: the tenant configures its own email provider (e.g. its own SMTP/MTA). The
        // tenant id comes from the authenticated key, never the path, so there is no cross-tenant id to
        // forge.
        app.MapPut("/v1/providers/{channel}", UpsertAsync).WithTags("Providers");
        return app;
    }

    private static async Task<IResult> UpsertAsync(
        string channel,
        SetProviderConfigRequest request,
        TenantContext tenant,
        ITenantProviderConfigStore store,
        IEnumerable<IEmailProvider> providers,
        ISmtpDestinationPolicy destinations,
        ISecretProtector protector,
        IClock clock,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        // v1 configures email only; the resolver has providers for no other channel yet, so writing one
        // would create a row nothing reads.
        if (!string.Equals(channel, "email", StringComparison.OrdinalIgnoreCase))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(channel)] = ["Only the email channel can be configured."]
            });

        var known = providers.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var providerName = request.Provider?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(providerName) || !known.Contains(providerName))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Provider)] = [$"Provider must be one of: {string.Join(", ", known)}."]
            });

        var settings = request.Settings ?? new Dictionary<string, string>();

        // Self-hosted SMTP lets the tenant pick the host, so apply the same rules the send path does: TLS
        // required and no internal destination. The failure is generic so the endpoint cannot be probed as
        // an internal-network oracle.
        if (providerName == "smtp")
        {
            var problem = await ValidateSmtpAsync(settings, destinations, cancellationToken);
            if (problem is not null)
                return problem;
        }

        var secretProtected = string.IsNullOrEmpty(request.Secret) ? null : protector.Protect(request.Secret);
        var config = new TenantProviderConfig(
            tenant.TenantId,
            NotificationChannel.Email,
            providerName,
            JsonSerializer.Serialize(settings),
            secretProtected,
            clock.UtcNow);

        await store.UpsertAsync(config, cancellationToken);

        // Audit the change without the secret: redirecting a tenant's mail is sensitive, so who and what
        // is recorded even though the value never is.
        loggerFactory.CreateLogger("Hiram.Api.Providers").LogInformation(
            "Provider config changed for tenant {TenantId} channel {Channel} to provider {Provider}",
            tenant.TenantId, NotificationChannel.Email, providerName);

        return Results.NoContent();
    }

    private static async Task<IResult?> ValidateSmtpAsync(
        IReadOnlyDictionary<string, string> settings,
        ISmtpDestinationPolicy destinations,
        CancellationToken cancellationToken)
    {
        var host = settings.GetValueOrDefault("host");
        if (string.IsNullOrWhiteSpace(host)
            || string.IsNullOrWhiteSpace(settings.GetValueOrDefault("from"))
            || !int.TryParse(settings.GetValueOrDefault("port"), out _))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["settings"] = ["SMTP requires host, port and from."]
            });
        }

        var security = settings.GetValueOrDefault("security")?.Trim().ToLowerInvariant();
        if (security is not ("starttls" or "ssl"))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["settings.security"] = ["Tenant SMTP requires TLS (starttls or ssl)."]
            });

        // Blocked and Unresolved collapse to one message on purpose: distinguishing them would tell a
        // caller which internal hosts exist.
        if (await destinations.InspectAsync(host, cancellationToken) != SmtpDestinationVerdict.Allowed)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["settings.host"] = ["Destination is not allowed."]
            });

        return null;
    }
}
