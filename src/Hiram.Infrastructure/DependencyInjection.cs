using Hiram.Application.Abstractions;
using Hiram.Application.Delivery;
using Hiram.Application.Notifications;
using Hiram.Application.Tenancy;
using Hiram.Infrastructure.Caching;
using Hiram.Infrastructure.Delivery;
using Hiram.Infrastructure.Persistence;
using Hiram.Infrastructure.Security;
using Hiram.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
        services.AddScoped<ITenantProviderConfigStore, TenantProviderConfigStore>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IEmailProvider, SmtpEmailProvider>();
        services.AddHttpClient<IEmailProvider, ResendEmailProvider>(client =>
            client.BaseAddress = new Uri("https://api.resend.com/"));

        return services;
    }

    // Wired only into the dispatcher, the host that runs the send pipeline. The resolver is scoped so it
    // reads the tenant config through a request scoped DbContext and gets a fresh Resend client per message.
    public static IServiceCollection AddHiramEmailDelivery(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDataProtection();
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();

        var platform = configuration.GetSection("Hiram:Email:Platform");
        var settings = platform.GetSection("Settings").GetChildren().ToDictionary(child => child.Key, child => child.Value ?? string.Empty);
        services.AddSingleton(new PlatformEmailDefaults(
            platform["Provider"] ?? "smtp",
            platform["Secret"],
            settings));

        services.AddScoped<EmailProviderResolver>();
        services.AddSingleton(EmailDeliveryPipeline.Build());

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
