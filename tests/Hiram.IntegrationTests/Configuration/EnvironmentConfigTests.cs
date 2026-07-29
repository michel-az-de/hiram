using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Hiram.IntegrationTests.Configuration;

// Guards that the host needs nothing but environment variables to boot in production: no
// appsettings.Development.json, no user-secrets. The connection strings point nowhere because boot
// must not touch the backends, only bind configuration.
[Collection("ApiHost")]
public sealed class EnvironmentConfigTests : IDisposable
{
    public EnvironmentConfigTests()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__Hiram", "Host=localhost;Port=1;Database=hiram;Username=u;Password=p");
        Environment.SetEnvironmentVariable("Hiram__AdminKey", "env-only-admin");
        Environment.SetEnvironmentVariable("OTEL_SDK_DISABLED", "true");
    }

    [Fact]
    public async Task Host_Boots_FromEnvOnly()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Production"));

        var client = factory.CreateClient();
        var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__Hiram", null);
        Environment.SetEnvironmentVariable("Hiram__AdminKey", null);
        Environment.SetEnvironmentVariable("OTEL_SDK_DISABLED", null);
    }
}
