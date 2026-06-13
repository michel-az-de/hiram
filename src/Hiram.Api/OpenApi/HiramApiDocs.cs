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

            return Task.CompletedTask;
        }));

        return services;
    }

    // Dark theme aligned to the brand palette: navy background, gold accent.
    public const string ScalarCss = """
        .dark-mode {
          --scalar-background-1: #0A1428;
          --scalar-color-accent: #C9A227;
        }
        """;

    private const string Overview = """
        # Hiram API

        Multi-tenant notification platform. Phase F1 delivers the email channel end to end: hashed
        API keys, idempotent ingestion, two interchangeable providers, a resilient send pipeline and
        shadow mode.

        ## Handshake

        Calls to `/v1/notifications` authenticate with a tenant API key in the `X-Api-Key` header.
        Keys are issued through the provisional admin endpoints, themselves guarded by the operator
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
        `permanent_failure` and the notification ends in `failed`; it is not surfaced as a synchronous
        HTTP status. Inspect the outcome with `GET /v1/notifications/{id}`, which returns the attempts.

        ## Error catalog

        Synchronous errors use RFC 9457 ProblemDetails:

        | Status | When |
        |---|---|
        | 400 | Invalid body: missing recipient, subject or body, or an unknown channel. |
        | 401 | Missing, unknown or revoked `X-Api-Key`; invalid `X-Admin-Key` on admin routes. |
        | 404 | Notification not found for the authenticated tenant. |
        | 409 | An idempotency key conflict that could not be resolved to the original notification. |

        ## Admin endpoints (provisional)

        The `/v1/admin/*` routes exist only until the Portal (F5). They are guarded by the shared
        `X-Admin-Key` rather than tenant keys and must not be exposed publicly.
        """;
}
