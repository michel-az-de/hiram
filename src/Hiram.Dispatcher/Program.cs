using Hiram.Dispatcher;
using Hiram.Infrastructure;
using Hiram.Infrastructure.Messaging;
using Hiram.Infrastructure.Telemetry;

var builder = Host.CreateApplicationBuilder(args);

var postgres = builder.Configuration.GetConnectionString("Hiram")
    ?? throw new InvalidOperationException("Connection string 'Hiram' is not configured.");

builder.AddHiramTelemetry("hiram-dispatcher");

builder.Services.AddHiramInfrastructure(postgres, builder.Configuration["DataProtection:KeysPath"]);
builder.Services.AddHiramEmailDelivery(builder.Configuration);
builder.Services.AddHiramPush(builder.Configuration);
builder.Services.AddHiramMessageProcessors();
builder.Services.AddScoped<PostgresOutboxPump>();
builder.Services.AddHostedService<PostgresDispatcherWorker>();

var host = builder.Build();
host.Run();
