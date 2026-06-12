using Hiram.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace Hiram.IntegrationTests.Security;

public class DataProtectionSecretProtectorTests
{
    private static DataProtectionSecretProtector NewProtector()
    {
        var provider = new ServiceCollection()
            .AddDataProtection()
            .Services
            .BuildServiceProvider()
            .GetRequiredService<IDataProtectionProvider>();

        return new DataProtectionSecretProtector(provider);
    }

    [Fact]
    public void ProtectThenUnprotect_RoundTripsTheSecret()
    {
        var protector = NewProtector();

        var protectedValue = protector.Protect("re_secret_123");

        Assert.NotEqual("re_secret_123", protectedValue);
        Assert.Equal("re_secret_123", protector.Unprotect(protectedValue));
    }
}
