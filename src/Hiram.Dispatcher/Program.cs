using Hiram.Dispatcher;
using Hiram.Infrastructure;
using Hiram.Infrastructure.Messaging;
using Hiram.Infrastructure.Telemetry;

var builder = Host.CreateApplicationBuilder(args);

var postgres = builder.Configuration.GetConnectionString("Hiram")
    ?? throw new InvalidOperationException("Connection string 'Hiram' is not configured.");
var transport = builder.Configuration["Hiram:Messaging:Transport"] ?? "postgres";

builder.AddHiramTelemetry("hiram-dispatcher");

builder.Services.AddHiramInfrastructure(postgres, builder.Configuration["DataProtection:KeysPath"]);
builder.Services.AddHiramEmailDelivery(builder.Configuration);
builder.Services.AddHiramPush(builder.Configuration);

switch (transport.ToLowerInvariant())
{
    case "postgres":
        builder.Services.AddHiramMessageProcessors();
        builder.Services.AddScoped<PostgresOutboxPump>();
        builder.Services.AddHostedService<PostgresDispatcherWorker>();
        break;
    case "rabbitmq":
        var rabbitMq = builder.Configuration.GetConnectionString("RabbitMq")
            ?? throw new InvalidOperationException("Connection string 'RabbitMq' is not configured for the RabbitMQ transport.");
        builder.Services.AddHiramMessaging(rabbitMq);
        builder.Services.AddHostedService<OutboxRelayWorker>();
        builder.Services.AddHostedService<EmailConsumerWorker>();
        builder.Services.AddHostedService<EventConsumerWorker>();
        builder.Services.AddHostedService<PushConsumerWorker>();
        builder.Services.AddHostedService<WebhookConsumerWorker>();
        break;
    default:
        throw new InvalidOperationException($"Unsupported messaging transport '{transport}'. Use 'postgres' or 'rabbitmq'.");
}

var host = builder.Build();
host.Run();
