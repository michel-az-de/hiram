using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Hiram.Api.OpenApi;

internal static class HiramApiDocs
{
    public static IServiceCollection AddHiramOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
        {
            document.Info.Title = "Hiram API";
            document.Info.Version = "v1";
            document.Info.Description = Overview;

            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes["ApiKey"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                Name = "X-Api-Key",
                In = ParameterLocation.Header,
                Description = "Tenant API key issued through POST /v1/admin/api-keys."
            };
            document.Components.SecuritySchemes["AdminKey"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                Name = "X-Admin-Key",
                In = ParameterLocation.Header,
                Description = "Provisional operator key (Hiram:AdminKey) guarding the /v1/admin routes."
            };

            document.Tags = Tags;

            foreach (var path in document.Paths)
            {
                if (path.Value.Operations is not { } operations)
                    continue;

                var scheme = SecuritySchemeFor(path.Key);
                foreach (var operation in operations)
                {
                    if (scheme is not null)
                    {
                        operation.Value.Security =
                        [
                            new OpenApiSecurityRequirement
                            {
                                [new OpenApiSecuritySchemeReference(scheme, document)] = new List<string>()
                            }
                        ];
                    }

                    var key = (operation.Key.ToString()!.ToUpperInvariant(), path.Key);
                    if (Summaries.TryGetValue(key, out var summary))
                        operation.Value.Summary = summary;

                    if (key == ("POST", "/v1/notifications")
                        && operation.Value.RequestBody?.Content is { } content
                        && content.TryGetValue("application/json", out var media))
                    {
                        media.Example = SubmitExample();
                    }
                }
            }

            return Task.CompletedTask;
        }));

        return services;
    }

    // Tenant keys guard /v1, the operator key guards /v1/admin and the dev-only /demo helper, and
    // everything else (docs, health) is public.
    private static string? SecuritySchemeFor(string path) =>
        path.StartsWith("/v1/admin", StringComparison.Ordinal) || path.StartsWith("/demo/", StringComparison.Ordinal) ? "AdminKey"
        : path.StartsWith("/v1/", StringComparison.Ordinal) ? "ApiKey"
        : null;

    private static readonly Dictionary<(string Method, string Path), string> Summaries = new()
    {
        [("POST", "/v1/notifications")] = "Send a notification",
        [("GET", "/v1/notifications")] = "List notifications with cursor pagination",
        [("GET", "/v1/notifications/{id}")] = "Read a notification and its delivery attempts",
        [("POST", "/v1/notifications/{id}/replay")] = "Replay a dead-lettered notification",
        [("POST", "/v1/admin/tenants")] = "Create a tenant",
        [("POST", "/v1/admin/api-keys")] = "Issue a tenant API key",
        [("DELETE", "/v1/admin/api-keys/{id}")] = "Revoke an API key",
        [("POST", "/demo/bootstrap")] = "Provision the demo tenant and a fresh key"
    };

    private static JsonNode? SubmitExample() => JsonNode.Parse(
        """{"channel":"email","recipient":"ops@example.com","subject":"Order confirmed","body":"Your order is on the way."}""");

    private static HashSet<OpenApiTag> Tags =>
    [
        new() { Name = "Notifications", Description = "Send notifications and read delivery back. The core of the platform." },
        new() { Name = "Events", Description = "Ingest domain events for the platform to act on." },
        new() { Name = "Consent", Description = "Per-recipient consent records." },
        new() { Name = "Blocks", Description = "Suppression list: recipients that must not be contacted." },
        new() { Name = "Templates", Description = "Versioned, per-tenant message templates." },
        new() { Name = "Push", Description = "Web push subscriptions and the VAPID public key." },
        new() { Name = "Webhooks", Description = "Delivery status callbacks, signed with X-Hiram-Signature." },
        new() { Name = "Admin", Description = "Operator provisioning guarded by X-Admin-Key." },
        new() { Name = "Demo (development only)", Description = "Dev-only helper that provisions a demo tenant and key for the console." }
    ];

    // Full theme aligned to the design system: brand fonts and both the vault (dark) and stone (light)
    // palettes derived from docs/design/tokens.json.
    public const string ScalarCss = """
        @import url('https://fonts.googleapis.com/css2?family=IBM+Plex+Sans:wght@400;500;600&family=IBM+Plex+Mono:wght@400;500&display=swap');

        .scalar-app {
          --scalar-font: 'IBM Plex Sans', system-ui, sans-serif;
          --scalar-font-code: 'IBM Plex Mono', ui-monospace, monospace;
          --scalar-radius: 6px;
          --scalar-radius-lg: 10px;
        }

        .dark-mode {
          --scalar-background-1: #0A1428;
          --scalar-background-2: #0F1B30;
          --scalar-background-3: #142440;
          --scalar-color-1: #E8ECF4;
          --scalar-color-2: #94A3BD;
          --scalar-color-3: #6B7A99;
          --scalar-color-accent: #C9A227;
          --scalar-background-accent: rgba(201, 162, 39, .12);
          --scalar-border-color: rgba(179, 200, 231, .14);
        }

        .light-mode {
          --scalar-background-1: #F8F7F4;
          --scalar-background-2: #FFFFFF;
          --scalar-background-3: #F0EEE8;
          --scalar-color-1: #1C2433;
          --scalar-color-2: #6B675D;
          --scalar-color-3: #8A867A;
          --scalar-color-accent: #1B3A6B;
          --scalar-background-accent: rgba(27, 58, 107, .08);
          --scalar-border-color: #E2DFD6;
        }
        """;

    private const string Overview = """
        # Hiram API

        Internal multi-tenant notification gateway for our products and selected clients. The
        maintained core delivers email end to end with hashed API keys, durable idempotency,
        interchangeable providers, PostgreSQL outbox leases, retries, audit and replay.

        ## Handshake

        Calls to `/v1/notifications` authenticate with a tenant API key in the `X-Api-Key` header.
        Keys are issued through the operator admin endpoints, themselves guarded by the operator
        key `X-Admin-Key` (configured as `Hiram:AdminKey`).

        1. Create a tenant:

        ```bash
        curl -X POST http://localhost:3357/v1/admin/tenants \
          -H "X-Admin-Key: <admin-key>" -H "Content-Type: application/json" \
          -d '{"name":"easystok","deliveryMode":"shadow"}'
        ```

        2. Issue an API key. The clear key (`hk_live_...`) is returned only in this response:

        ```bash
        curl -X POST http://localhost:3357/v1/admin/api-keys \
          -H "X-Admin-Key: <admin-key>" -H "Content-Type: application/json" \
          -d '{"tenantId":"<tenant-id>","name":"easystok-server"}'
        ```

        3. Send a notification. An `Idempotency-Key` (scoped per tenant, 24h window) makes the call
        safe to retry:

        ```bash
        curl -i -X POST http://localhost:3357/v1/notifications \
          -H "X-Api-Key: hk_live_..." -H "Idempotency-Key: evt-0001" \
          -H "Content-Type: application/json" \
          -d '{"channel":"email","recipient":"ops@example.com","subject":"hello","body":"f1"}'
        ```

        A repeated `Idempotency-Key` returns the original notification id with HTTP 202 and the
        response header `Idempotency-Replayed: true`.

        ## Delivery

        Ingestion is asynchronous: a successful `POST` returns **202 Accepted** once the notification
        and its outbox row are committed in one transaction. The send runs in the background and is
        recorded as one delivery attempt per try (`sent`, `transient_failure`, `permanent_failure`,
        or `shadow_would_send` for shadow tenants). A provider rejection, such as a Resend 422, is a
        `permanent_failure`; after retries are exhausted, the notification ends in `dead_lettered`.
        This is not surfaced as a synchronous HTTP status. Inspect the outcome with
        `GET /v1/notifications/{id}`, which returns the attempts.

        ## Error catalog

        Synchronous errors use RFC 9457 ProblemDetails:

        | Status | When |
        |---|---|
        | 400 | Invalid body: missing recipient, subject or body, or an unknown channel. |
        | 401 | Missing, unknown or revoked `X-Api-Key`; invalid `X-Admin-Key` on admin routes. |
        | 404 | Notification not found for the authenticated tenant. |
        | 409 | An idempotency key conflict that could not be resolved to the original notification. |

        ## Admin endpoints

        The `/v1/admin/*` routes are the operator surface. They are guarded by the shared
        `X-Admin-Key` rather than tenant keys and must not be exposed publicly.
        """;
}
