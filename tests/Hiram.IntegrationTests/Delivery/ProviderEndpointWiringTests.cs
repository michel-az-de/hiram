using Hiram.Application.Delivery;
using Hiram.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hiram.IntegrationTests.Delivery;

// The address a provider talks to is part of its identity: pointing the Resend adapter at Twilio's host
// produces a 4xx that reads like a credential problem. These tests pin one named client per provider, so
// no two adapters can share a base address by accident, and pin that the address comes from configuration
// so a local double can stand in for the real API without touching the delivery code.
public class ProviderEndpointWiringTests
{
    private const string DummyConnection = "Host=localhost;Database=hiram;Username=hiram;Password=hiram";

    private static ServiceProvider Build(IConfiguration? configuration = null)
    {
        var services = new ServiceCollection();
        if (configuration is not null)
            services.AddHiramProviderEndpoints(configuration);

        services.AddHiramInfrastructure(DummyConnection);
        return services.BuildServiceProvider();
    }

    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)))
            .Build();

    [Fact]
    public void EachProvider_GetsItsOwnNamedHttpClient()
    {
        using var provider = Build();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        Assert.Equal(new Uri("https://api.resend.com/"), factory.CreateClient("resend").BaseAddress);
        Assert.Equal(new Uri("https://comms.twilio.com/v1/"), factory.CreateClient("twilio-email").BaseAddress);
        Assert.Equal(new Uri("https://api.twilio.com/"), factory.CreateClient("twilio-sms").BaseAddress);
        Assert.Equal(new Uri("https://api.twilio.com/"), factory.CreateClient("twilio-whatsapp").BaseAddress);
    }

    [Fact]
    public void NoProvider_HangsItsConfigurationOnTheInterfaceName()
    {
        // AddHttpClient<TClient, TImplementation> derives the logical name from TClient, so two adapters
        // behind the same port land on one client and the last base address configured wins for both.
        using var provider = Build();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        Assert.Null(factory.CreateClient("IEmailProvider").BaseAddress);
        Assert.Null(factory.CreateClient("ISmsProvider").BaseAddress);
        Assert.Null(factory.CreateClient("IWhatsAppProvider").BaseAddress);
    }

    [Fact]
    public void Endpoints_FallBackToProduction_WhenNotConfigured()
    {
        using var provider = Build(Configuration());

        Assert.Equal(ProviderEndpoints.Production, provider.GetRequiredService<ProviderEndpoints>());
    }

    [Fact]
    public void Endpoints_ComeFromConfiguration_WhenSet()
    {
        var configuration = Configuration(
            ("Hiram:Providers:Endpoints:Resend", "http://localhost:4010/resend/"),
            ("Hiram:Providers:Endpoints:TwilioEmail", "http://localhost:4010/comms/v1/"),
            ("Hiram:Providers:Endpoints:TwilioApi", "http://localhost:4010/twilio/"));

        using var provider = Build(configuration);
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        Assert.Equal(new Uri("http://localhost:4010/resend/"), factory.CreateClient("resend").BaseAddress);
        Assert.Equal(new Uri("http://localhost:4010/comms/v1/"), factory.CreateClient("twilio-email").BaseAddress);
        Assert.Equal(new Uri("http://localhost:4010/twilio/"), factory.CreateClient("twilio-sms").BaseAddress);
        Assert.Equal(new Uri("http://localhost:4010/twilio/"), factory.CreateClient("twilio-whatsapp").BaseAddress);
    }

    [Theory]
    // A bare path is the accident to catch. It is not an absolute URI on Windows, and on Unix the parser
    // accepts it as an absolute file URI, so checking only for absoluteness passes on one platform and
    // fails on the other while both end up with a base address that reaches no provider.
    [InlineData("/twilio/")]
    [InlineData("localhost:4010")]
    [InlineData("file:///tmp/twilio/")]
    [InlineData("ftp://provider.invalid/")]
    public void Endpoints_RejectAnAddressThatIsNotHttp(string configured)
    {
        var configuration = Configuration(("Hiram:Providers:Endpoints:TwilioApi", configured));

        // Failing at startup names the offending key; failing at delivery time would name a transport
        // error and send whoever is on call looking at the provider instead of at the configuration.
        var error = Assert.Throws<InvalidOperationException>(() => Build(configuration));
        Assert.Contains("Hiram:Providers:Endpoints:TwilioApi", error.Message, StringComparison.Ordinal);
    }
}
