using Hiram.Application.Abstractions;
using Hiram.Application.Notifications;
using Hiram.Infrastructure.Persistence;
using Hiram.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hiram.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddHiramInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<HiramDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<INotificationStore, NotificationStore>();
        services.AddScoped<INotificationReader, NotificationReader>();
        services.AddSingleton<IClock, SystemClock>();

        return services;
    }
}
