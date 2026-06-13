using Hiram.Dispatcher;
using Hiram.Infrastructure;
using Hiram.Infrastructure.Messaging;
using Hiram.Infrastructure.Telemetry;

var builder = Host.CreateApplicationBuilder(args);

var postgres = builder.Configuration.GetConnectionString("Hiram")
    ?? throw new InvalidOperationException("Connection string 'Hiram' is not configured.");
var rabbitMq = builder.Configuration.GetConnectionString("RabbitMq")
    ?? throw new InvalidOperationException("Connection string 'RabbitMq' is not configured.");

builder.AddHiramTelemetry("hiram-dispatcher");

builder.Services.AddHiramInfrastructure(postgres);
builder.Services.AddHiramEmailDelivery(builder.Configuration);
builder.Services.AddHiramMessaging(rabbitMq);
builder.Services.AddHostedService<OutboxRelayWorker>();
builder.Services.AddHostedService<EmailConsumerWorker>();

var host = builder.Build();
host.Run();
