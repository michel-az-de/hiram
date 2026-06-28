using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Hiram.IntegrationTests.Health;

// Dependencies point at unreachable endpoints, so readiness fails without needing real backends and
// liveness still answers. Both run Docker-free.
[Collection("ApiHost")]
public sealed class HealthCheckTests : IDisposable
{
    public HealthCheckTests()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__Hiram", "Host=localhost;Port=1;Database=hiram;Username=u;Password=p;Timeout=2;Command Timeout=2");
        Environment.SetEnvironmentVariable("ConnectionStrings__Redis", "localhost:6390,abortConnect=false,connectTimeout=500");
        Environment.SetEnvironmentVariable("Hiram__AdminKey", "health-admin");
        Environment.SetEnvironmentVariable("OTEL_SDK_DISABLED", "true");
    }

    [Fact]
    public async Task Ready_Returns503_WhenDependencyDown()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Live_Returns200_EvenWhenDependencyDown()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__Hiram", null);
        Environment.SetEnvironmentVariable("ConnectionStrings__Redis", null);
        Environment.SetEnvironmentVariable("Hiram__AdminKey", null);
        Environment.SetEnvironmentVariable("OTEL_SDK_DISABLED", null);
    }
}
