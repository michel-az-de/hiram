using Hiram.Infrastructure;
using Hiram.Infrastructure.Messaging;

namespace Hiram.Api.Workers;

public static class WorkerDependencyInjection
{
    public static IServiceCollection AddHiramWorkers(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddHiramEmailDelivery(configuration);
        services.AddHiramPush(configuration);
        services.AddHiramMessageProcessors();

        if (!configuration.GetValue("Hiram:Workers:Enabled", true))
            return services;

        services.AddScoped<PostgresOutboxPump>();
        services.AddHostedService<OutboxWorker>();
        return services;
    }
}
