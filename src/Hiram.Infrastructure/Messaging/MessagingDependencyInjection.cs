using Microsoft.Extensions.DependencyInjection;

namespace Hiram.Infrastructure.Messaging;

public static class MessagingDependencyInjection
{
    public static IServiceCollection AddHiramMessageProcessors(this IServiceCollection services)
    {
        services.AddScoped<ChannelDeliveryProcessor>();
        services.AddScoped<EmailChannelDelivery>();
        services.AddScoped<PushChannelDelivery>();
        services.AddScoped<SmsChannelDelivery>();
        services.AddScoped<WhatsAppChannelDelivery>();
        services.AddScoped<EventFanout>();
        services.AddScoped<EventMessageProcessor>();
        services.AddHttpClient<WebhookDeliveryProcessor>();
        services.AddScoped<OutboxMessageDispatcher>();

        return services;
    }
}
