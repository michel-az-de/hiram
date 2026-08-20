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
using Microsoft.Extensions.DependencyInjection.Extensions;

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

        // Whoever composes the host may have configured the endpoints already; production values stand in
        // otherwise, so an environment that configures nothing still talks to the real providers.
        services.TryAddSingleton(ProviderEndpoints.Production);

        // One named client per adapter, never per port. AddHttpClient<TClient, TImplementation> derives the
        // logical name from TClient, so two adapters behind one port share a client and the last base
        // address configured wins for both: that is how the Resend adapter ended up posting to Twilio's
        // host (issue #139).
        services.AddProviderClient(ProviderNames.Resend);
        services.AddProviderClient(ProviderNames.TwilioEmail);
        services.AddProviderClient(ProviderNames.TwilioSms);
        services.AddProviderClient(ProviderNames.TwilioWhatsApp);
        services.AddProviderClient(ProviderNames.MetaWhatsApp);

        services.AddTransient<IEmailProvider>(provider => new ResendEmailProvider(provider.ClientFor(ProviderNames.Resend)));
        services.AddTransient<IEmailProvider>(provider => new TwilioEmailProvider(provider.ClientFor(ProviderNames.TwilioEmail)));
        services.AddTransient<ISmsProvider>(provider => new TwilioSmsProvider(provider.ClientFor(ProviderNames.TwilioSms)));
        services.AddTransient<IWhatsAppProvider>(provider => new TwilioWhatsAppProvider(provider.ClientFor(ProviderNames.TwilioWhatsApp)));
        services.AddTransient<IWhatsAppProvider>(provider => new MetaWhatsAppProvider(
            provider.ClientFor(ProviderNames.MetaWhatsApp),
            provider.GetRequiredService<ProviderEndpoints>().MetaGraphVersion));

        return services;
    }

    // Reads the provider endpoints from configuration, falling back to production per key. Call it before
    // AddHiramInfrastructure: the infrastructure registration only fills in what is still missing.
    public static IServiceCollection AddHiramProviderEndpoints(this IServiceCollection services, IConfiguration configuration)
    {
        var endpoints = configuration.GetSection("Hiram:Providers:Endpoints");
        services.AddSingleton(new ProviderEndpoints(
            Absolute(endpoints, "Resend", ProviderEndpoints.Production.Resend),
            Absolute(endpoints, "TwilioEmail", ProviderEndpoints.Production.TwilioEmail),
            Absolute(endpoints, "TwilioApi", ProviderEndpoints.Production.TwilioApi),
            Absolute(endpoints, "MetaGraph", ProviderEndpoints.Production.MetaGraph),
            endpoints["MetaGraphVersion"] is { Length: > 0 } version
                ? version
                : ProviderEndpoints.Production.MetaGraphVersion));

        return services;
    }

    private static IServiceCollection AddProviderClient(this IServiceCollection services, string providerName) =>
        services.AddHttpClient(
            providerName,
            (provider, client) => client.BaseAddress = AddressFor(provider.GetRequiredService<ProviderEndpoints>(), providerName))
            .Services;

    // One place maps a provider name to a host, so a new adapter cannot quietly inherit another one's.
    private static Uri AddressFor(ProviderEndpoints endpoints, string providerName) => providerName switch
    {
        ProviderNames.Resend => endpoints.Resend,
        ProviderNames.TwilioEmail => endpoints.TwilioEmail,
        ProviderNames.TwilioSms or ProviderNames.TwilioWhatsApp => endpoints.TwilioApi,
        ProviderNames.MetaWhatsApp => endpoints.MetaGraph,
        _ => throw new InvalidOperationException($"No endpoint is mapped for the provider '{providerName}'.")
    };

    private static HttpClient ClientFor(this IServiceProvider services, string providerName) =>
        services.GetRequiredService<IHttpClientFactory>().CreateClient(providerName);

    // A bad address turns every send into a request against nothing, and the failure would surface as a
    // transport error at delivery time. Failing at startup names the offending key instead.
    //
    // The scheme is part of the check, not decoration: on Unix the Uri parser accepts a bare path as an
    // absolute file URI, so "/twilio/" passes an absolute-only test there and fails it on Windows. A
    // provider endpoint is reached over HTTP, and nothing else is a valid answer on any platform.
    private static Uri Absolute(IConfigurationSection endpoints, string key, Uri fallback)
    {
        var configured = endpoints[key];
        if (string.IsNullOrWhiteSpace(configured))
            return fallback;

        if (!Uri.TryCreate(configured, UriKind.Absolute, out var address)
            || (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException(
                $"{endpoints.Path}:{key} must be an absolute http or https URI, and '{configured}' is not.");

        return address;
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
