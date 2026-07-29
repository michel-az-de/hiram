using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hiram.Infrastructure.Messaging;

public static class MessagingDependencyInjection
{
    public static IServiceCollection AddHiramMessageProcessors(this IServiceCollection services)
    {
        services.AddScoped<EmailNotificationProcessor>();
        services.AddScoped<EventFanout>();
        services.AddScoped<EventMessageProcessor>();
        services.AddScoped<PushNotificationProcessor>();
        services.AddHttpClient<WebhookDeliveryProcessor>();
        services.AddScoped<OutboxMessageDispatcher>();

        return services;
    }

    public static IServiceCollection AddHiramMessaging(this IServiceCollection services, string connectionString)
    {
        services.AddHiramMessageProcessors();
        services.AddSingleton(sp => new RabbitMqConnection(connectionString, sp.GetRequiredService<ILogger<RabbitMqConnection>>()));
        services.AddScoped<OutboxRelay>();

        return services;
    }
}
