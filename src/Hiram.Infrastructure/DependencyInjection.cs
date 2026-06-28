using Hiram.Application.Abstractions;
using Hiram.Application.Delivery;
using Hiram.Application.Blocks;
using Hiram.Application.Consents;
using Hiram.Application.Events;
using Hiram.Application.Routines;
using Hiram.Application.Metering;
using Hiram.Application.Notifications;
using Hiram.Application.Push;
using Hiram.Application.Tenancy;
using Hiram.Application.Templates;
using Hiram.Application.Webhooks;
using Hiram.Domain.Notifications;
using Hiram.Infrastructure.Caching;
using Hiram.Infrastructure.Delivery;
using Hiram.Infrastructure.Persistence;
using Hiram.Infrastructure.Push;
using Hiram.Infrastructure.Security;
using Hiram.Infrastructure.Templates;
using Hiram.Infrastructure.Time;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Hiram.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddHiramInfrastructure(this IServiceCollection services, string connectionString, string? dataProtectionKeyRingPath = null)
    {
        services.AddDbContext<HiramDbContext>(options => options.UseNpgsql(connectionString));
        services.AddHiramDataProtection(dataProtectionKeyRingPath);
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();
        services.AddScoped<INotificationStore, NotificationStore>();
        services.AddScoped<IEventStore, EventStore>();
        services.AddScoped<IRoutineCatalog, RoutineCatalog>();
        services.AddScoped<ITemplateApprovalLookup, TemplateApprovalLookup>();
        services.AddScoped<RoutineResolver>();
        services.AddScoped<IConsentStore, ConsentStore>();
        services.AddScoped<ConsentPolicy>();
        services.AddScoped<ConsentReconciler>();
        services.AddScoped<IBlockStore, BlockStore>();
        services.AddScoped<BlockGate>();
        services.AddScoped<INotificationReader, NotificationReader>();
        services.AddScoped<IDeadLetterReplay, DeadLetterReplay>();
        services.AddScoped<ITemplateStore, TemplateStore>();
        services.AddSingleton<ITemplateRenderer, ScribanTemplateRenderer>();
        services.AddScoped<IPushSubscriptionStore, PushSubscriptionStore>();
        services.AddScoped<IWebhookEndpointStore, WebhookEndpointStore>();
        services.AddScoped<ITenantStore, TenantStore>();
        services.AddScoped<IApiKeyStore, ApiKeyStore>();
        services.AddScoped<ITenantProviderConfigStore, TenantProviderConfigStore>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IEmailProvider, SmtpEmailProvider>();
        services.AddHttpClient<IEmailProvider, ResendEmailProvider>(client =>
            client.BaseAddress = new Uri("https://api.resend.com/"));

        return services;
    }

    public static IServiceCollection AddHiramDataProtection(this IServiceCollection services, string? keyRingPath = null)
    {
        // Api and dispatcher are separate processes: they must share the same key ring and the same
        // application discriminator, otherwise the dispatcher cannot decrypt the tenant secrets the
        // api encrypted. The key ring path is a shared volume in production.
        var dataProtection = services.AddDataProtection().SetApplicationName("hiram");
        if (!string.IsNullOrWhiteSpace(keyRingPath))
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));

        return services;
    }

    // Wired only into the dispatcher, the host that runs the send pipeline. The resolver is scoped so it
    // reads the tenant config through a request scoped DbContext and gets a fresh Resend client per message.
    public static IServiceCollection AddHiramEmailDelivery(this IServiceCollection services, IConfiguration configuration)
    {
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

    public static IServiceCollection AddHiramPush(this IServiceCollection services, IConfiguration configuration)
    {
        var vapid = configuration.GetSection("Hiram:Push:Vapid");
        services.AddSingleton(new PushVapidOptions(
            vapid["Subject"] ?? "mailto:admin@hiram.local",
            vapid["PublicKey"] ?? string.Empty,
            vapid["PrivateKey"] ?? string.Empty));
        services.AddHttpClient<IPushSender, WebPushSender>();

        return services;
    }

    public static IServiceCollection AddHiramMetering(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection("Hiram:Metering");

        var channelBase = new Dictionary<NotificationChannel, long>();
        foreach (var child in section.GetSection("ChannelBase").GetChildren())
        {
            if (Enum.TryParse<NotificationChannel>(child.Key, ignoreCase: true, out var channel) && long.TryParse(child.Value, out var value))
                channelBase[channel] = value;
        }

        services.AddSingleton(new CreditRates(channelBase, section.GetValue("DefaultBase", 1L), section.GetValue("PerKilobyte", 1L)));
        services.AddSingleton<ICreditCalculator, CreditCalculator>();

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
