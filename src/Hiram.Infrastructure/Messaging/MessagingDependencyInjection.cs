using Microsoft.Extensions.DependencyInjection;

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
}
