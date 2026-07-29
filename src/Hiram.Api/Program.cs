using Hiram.Api.Admin;
using Hiram.Api.Authentication;
using Hiram.Api.Blocks;
using Hiram.Api.Consents;
using Hiram.Api.Demo;
using Hiram.Api.Events;
using Hiram.Api.Health;
using Hiram.Api.Notifications;
using Hiram.Api.OpenApi;
using Hiram.Api.Providers;
using Hiram.Api.Push;
using Hiram.Api.Templates;
using Hiram.Api.Webhooks;
using Hiram.Application.Events;
using Hiram.Application.Notifications;
using Hiram.Infrastructure;
using Hiram.Infrastructure.Persistence;
using Hiram.Infrastructure.Telemetry;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Hiram")
    ?? throw new InvalidOperationException("Connection string 'Hiram' is not configured.");

// Production runs migrations as a separate --migrate-only job, never on a serving replica, because
// several replicas starting at once would race without an advisory lock. --dry-run prints the
// idempotent script for review without touching the database.
if (args.Contains("--migrate-only"))
{
    var migrationOptions = new DbContextOptionsBuilder<HiramDbContext>().UseNpgsql(connectionString).Options;
    await using var migrationContext = new HiramDbContext(migrationOptions);
    if (args.Contains("--dry-run"))
        await Console.Out.WriteAsync(HiramSchema.GenerateScript(migrationContext));
    else
        await HiramSchema.ApplyAsync(migrationContext);
    return 0;
}

builder.AddHiramTelemetry("hiram-api", tracing => tracing
    .AddAspNetCoreInstrumentation()
    .AddHttpClientInstrumentation());

builder.Services.AddProblemDetails();
builder.Services.AddHiramOpenApi();
builder.Services.AddHiramInfrastructure(connectionString, builder.Configuration["DataProtection:KeysPath"]);
builder.Services.AddHiramPush(builder.Configuration);
builder.Services.AddScoped<ISubmitNotification, SubmitNotificationHandler>();
builder.Services.AddScoped<IIngestEvent, IngestEventHandler>();
builder.Services.AddScoped<TenantContext>();
builder.Services.AddHealthChecks()
    .AddCheck<PostgresHealthCheck>("postgres", tags: ["ready"]);

var app = builder.Build();

if (builder.Configuration.GetValue<bool>("Hiram:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var database = scope.ServiceProvider.GetRequiredService<HiramDbContext>();
    await HiramSchema.ApplyAsync(database);
}

// In development and in the public demo environment (ADR-022) the api also serves the static learn
// hub and demo console from site/, on the same origin as /scalar so the browser console can reach
// /v1 without CORS. The same gate exposes POST /demo/bootstrap, still behind X-Admin-Key, so the
// operator can mint the demo tenant and key. The gate keeps this surface out of production, and
// mounting site/ at the root mirrors the eventual static publish so the pages resolve ../styles.css
// exactly as they will when hosted standalone.
if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Demo"))
{
    var siteRoot = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", "..", "site"));
    if (Directory.Exists(siteRoot))
    {
        var site = new PhysicalFileProvider(siteRoot);
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = site });
        app.UseStaticFiles(new StaticFileOptions { FileProvider = site });
    }

    app.MapDemoBootstrapEndpoint();
}

app.UseMiddleware<ApiKeyMiddleware>();

app.MapOpenApi();
app.MapScalarApiReference(options => options
    .WithTitle("Hiram API")
    .EnableDarkMode()
    .WithCustomCss(HiramApiDocs.ScalarCss));
app.MapAdminEndpoints();
app.MapNotificationEndpoints();
app.MapEventEndpoints();
app.MapConsentEndpoints();
app.MapBlockEndpoints();
app.MapProviderConfigEndpoints();
app.MapTemplateEndpoints();
app.MapPushEndpoints();
app.MapWebhookEndpoints();

// Liveness runs no checks: the process is up. Readiness gates on the dependencies the api needs.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.Run();

return 0;

public partial class Program;
