using Hiram.Api.Authentication;
using Hiram.Api.Notifications;
using Hiram.Application.Notifications;
using Hiram.Infrastructure;
using Hiram.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Hiram")
    ?? throw new InvalidOperationException("Connection string 'Hiram' is not configured.");

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddHiramInfrastructure(connectionString);
builder.Services.AddScoped<ISubmitNotification, SubmitNotificationHandler>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    var database = scope.ServiceProvider.GetRequiredService<HiramDbContext>();
    await database.Database.MigrateAsync();
}

app.UseMiddleware<ApiKeyMiddleware>();

app.MapOpenApi();
app.MapScalarApiReference();
app.MapNotificationEndpoints();

app.Run();

public partial class Program;
