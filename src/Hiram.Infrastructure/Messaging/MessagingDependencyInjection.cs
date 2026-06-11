using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hiram.Infrastructure.Messaging;

public static class MessagingDependencyInjection
{
    public static IServiceCollection AddHiramMessaging(this IServiceCollection services, string connectionString)
    {
        services.AddSingleton(sp => new RabbitMqConnection(connectionString, sp.GetRequiredService<ILogger<RabbitMqConnection>>()));
        services.AddScoped<OutboxRelay>();
        services.AddScoped<EmailNotificationProcessor>();

        return services;
    }
}
