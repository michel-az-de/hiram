using Hiram.Api.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Hiram.IntegrationTests.Configuration;

public sealed class WorkerRegistrationTests
{
    [Fact]
    public void AddHiramWorkers_RegistersPostgresWorkerByDefault()
    {
        var services = new ServiceCollection();

        services.AddHiramWorkers(new ConfigurationBuilder().Build());

        Assert.Contains(services, IsPostgresWorker);
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(PostgresOutboxPump));
    }

    [Fact]
    public void AddHiramWorkers_DoesNotRegisterPostgresWorkerWhenDisabled()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Hiram:Workers:Enabled"] = "false"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();

        services.AddHiramWorkers(configuration);

        Assert.DoesNotContain(services, IsPostgresWorker);
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(PostgresOutboxPump));
    }

    private static bool IsPostgresWorker(ServiceDescriptor descriptor) =>
        descriptor.ServiceType == typeof(IHostedService)
        && descriptor.ImplementationType == typeof(OutboxWorker);
}
