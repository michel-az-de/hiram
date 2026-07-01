using System.Net;
using Hiram.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace Hiram.IntegrationTests.Documentation;

[Collection("ApiHost")]
public class ApiDocsTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine").Build();
    private WebApplicationFactory<Program>? _factory;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        Environment.SetEnvironmentVariable("ConnectionStrings__Hiram", _postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("ConnectionStrings__Redis", _redis.GetConnectionString());
        Environment.SetEnvironmentVariable("OTEL_SDK_DISABLED", "true");

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();

        Environment.SetEnvironmentVariable("ConnectionStrings__Hiram", null);
        Environment.SetEnvironmentVariable("ConnectionStrings__Redis", null);
        Environment.SetEnvironmentVariable("OTEL_SDK_DISABLED", null);

        await _postgres.DisposeAsync();
        await _redis.DisposeAsync();
    }

    [Fact]
    public async Task OpenApiDocument_DescribesHandshakeAndSecuritySchemes()
    {
        var client = _factory!.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        var document = await response.Content.ReadAsStringAsync();

        Assert.Contains("Handshake", document);
        Assert.Contains("X-Api-Key", document);
        Assert.Contains("X-Admin-Key", document);
        Assert.Contains("Idempotency-Replayed", document);
    }

    [Fact]
    public async Task LearnHub_IsServedInDevelopmentWithoutApiKey()
    {
        var client = _factory!.CreateClient();

        var response = await client.GetAsync("/learn/index.html");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Hiram", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SharedStylesheet_IsReachableForLearnPages()
    {
        var client = _factory!.CreateClient();

        var response = await client.GetAsync("/styles.css");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/css", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task LearnHub_IsNotServedInProduction()
    {
        await using var production = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        var client = production.CreateClient();

        var response = await client.GetAsync("/learn/index.html");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
