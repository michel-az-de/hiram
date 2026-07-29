using Hiram.Application.Abstractions;
using Hiram.Application.Delivery;
using Hiram.Application.Blocks;
using Hiram.Application.Consents;
using Hiram.Application.Events;
using Hiram.Application.Messaging;
using Hiram.Application.Routines;
using Hiram.Application.Notifications;
using Hiram.Application.Outbox;
using Hiram.Application.Push;
using Hiram.Application.Scheduling;
using Hiram.Application.Tenancy;
using Hiram.Application.Templates;
using Hiram.Application.Webhooks;
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

namespace Hiram.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddHiramInfrastructure(this IServiceCollection services, string connectionString, string? dataProtectionKeyRingPath = null)
    {
        services.AddDbContext<HiramDbContext>(options => options.UseNpgsql(connectionString));
        services.AddHiramDataProtection(dataProtectionKeyRingPath);
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();
        services.AddScoped<INotificationStore, NotificationStore>();
        services.AddScoped<IOutboxQueue, OutboxQueue>();
        services.AddScoped<IEventStore, EventStore>();
        services.AddScoped<IRoutineCatalog, RoutineCatalog>();
        services.AddScoped<IRoutineStore, RoutineStore>();
        services.AddScoped<ITemplateApprovalLookup, TemplateApprovalLookup>();
        services.AddScoped<RoutineResolver>();
        services.AddScoped<IConsentStore, ConsentStore>();
        services.AddScoped<ConsentPolicy>();
        services.AddScoped<ConsentReconciler>();
        services.AddScoped<IBlockStore, BlockStore>();
        services.AddScoped<BlockGate>();
        services.AddScoped<ChannelResolver>();
        services.AddSingleton(new WindowScheduler(() => TimeSpan.FromSeconds(Random.Shared.Next(0, 300))));
        services.AddSingleton<DailyLimitPolicy>();
        services.AddScoped<IMessageClaimStore, MessageClaimStore>();
        services.AddScoped<MessageDispatchGuard>();
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
        services.AddSingleton<ISmtpDestinationPolicy, SmtpDestinationPolicy>();
        services.AddSingleton<IEmailProvider, SmtpEmailProvider>();
        services.AddHttpClient<IEmailProvider, ResendEmailProvider>(client =>
            client.BaseAddress = new Uri("https://api.resend.com/"));

        return services;
    }

    public static IServiceCollection AddHiramDataProtection(this IServiceCollection services, string? keyRingPath = null)
    {
        // Replicas must share the same key ring and application discriminator, otherwise a request
        // can write a tenant secret that another instance cannot decrypt.
        var dataProtection = services.AddDataProtection().SetApplicationName("hiram");
        if (!string.IsNullOrWhiteSpace(keyRingPath))
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));

        return services;
    }

    // The resolver is scoped so it reads the tenant config through a request scoped DbContext and gets
    // a fresh Resend client per message.
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

}
