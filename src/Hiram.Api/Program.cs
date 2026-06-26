using Hiram.Api.Admin;
using Hiram.Api.Authentication;
using Hiram.Api.Notifications;
using Hiram.Api.OpenApi;
using Hiram.Api.Templates;
using Hiram.Application.Notifications;
using Hiram.Infrastructure;
using Hiram.Infrastructure.Persistence;
using Hiram.Infrastructure.Telemetry;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Hiram")
    ?? throw new InvalidOperationException("Connection string 'Hiram' is not configured.");
var redisConnectionString = builder.Configuration.GetConnectionString("Redis")
    ?? throw new InvalidOperationException("Connection string 'Redis' is not configured.");

builder.AddHiramTelemetry("hiram-api", tracing => tracing
    .AddAspNetCoreInstrumentation()
    .AddHttpClientInstrumentation());

builder.Services.AddProblemDetails();
builder.Services.AddHiramOpenApi();
builder.Services.AddHiramInfrastructure(connectionString);
builder.Services.AddHiramRedis(redisConnectionString);
builder.Services.AddScoped<ISubmitNotification, SubmitNotificationHandler>();
builder.Services.AddScoped<TenantContext>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    var database = scope.ServiceProvider.GetRequiredService<HiramDbContext>();
    await database.Database.MigrateAsync();
}

app.UseMiddleware<ApiKeyMiddleware>();

app.MapOpenApi();
app.MapScalarApiReference(options => options
    .WithTitle("Hiram API")
    .EnableDarkMode()
    .WithCustomCss(HiramApiDocs.ScalarCss));
app.MapAdminEndpoints();
app.MapNotificationEndpoints();
app.MapTemplateEndpoints();

app.Run();

public partial class Program;
