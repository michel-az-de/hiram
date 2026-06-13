using Hiram.Application.Abstractions;
using Hiram.Application.Delivery;
using Hiram.Application.Notifications;
using Hiram.Application.Tenancy;
using Hiram.Infrastructure.Caching;
using Hiram.Infrastructure.Delivery;
using Hiram.Infrastructure.Persistence;
using Hiram.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Hiram.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddHiramInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<HiramDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<INotificationStore, NotificationStore>();
        services.AddScoped<INotificationReader, NotificationReader>();
        services.AddScoped<ITenantStore, TenantStore>();
        services.AddScoped<IApiKeyStore, ApiKeyStore>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IEmailProvider, SmtpEmailProvider>();
        services.AddHttpClient<IEmailProvider, ResendEmailProvider>(client =>
            client.BaseAddress = new Uri("https://api.resend.com/"));

        return services;
    }

    public static IServiceCollection AddHiramRedis(this IServiceCollection services, string connectionString)
    {
        var options = ConfigurationOptions.Parse(connectionString);
        // Authentication must survive a Redis outage, so a failed connection degrades the throttle
        // instead of aborting the host on startup.
        options.AbortOnConnectFail = false;

        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(options));
        services.AddSingleton<IApiKeyUsageThrottle, RedisApiKeyUsageThrottle>();
        services.AddSingleton<IIdempotencyKeys, RedisIdempotencyKeys>();

        return services;
    }
}
