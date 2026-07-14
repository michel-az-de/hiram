using Hiram.Application.Delivery;
using Hiram.Infrastructure.Delivery;

namespace Hiram.IntegrationTests.Delivery;

public class SmtpDestinationPolicyTests
{
    private readonly SmtpDestinationPolicy _policy = new();

    [Theory]
    [InlineData("127.0.0.1")]        // loopback
    [InlineData("169.254.169.254")]  // link-local, cloud metadata
    [InlineData("10.0.0.5")]         // RFC1918
    [InlineData("192.168.1.10")]     // RFC1918
    [InlineData("172.16.0.1")]       // RFC1918
    [InlineData("100.64.0.1")]       // CGNAT
    public async Task Inspect_Blocks_InternalAddresses(string host)
    {
        Assert.Equal(SmtpDestinationVerdict.Blocked, await _policy.InspectAsync(host, CancellationToken.None));
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("93.184.216.34")]
    public async Task Inspect_Allows_PublicAddresses(string host)
    {
        Assert.Equal(SmtpDestinationVerdict.Allowed, await _policy.InspectAsync(host, CancellationToken.None));
    }

    [Fact]
    public async Task Inspect_Unresolved_WhenNameDoesNotResolve()
    {
        // .invalid is reserved (RFC 2606) and never resolves, so this is a transient, not a block.
        Assert.Equal(
            SmtpDestinationVerdict.Unresolved,
            await _policy.InspectAsync("no-such-host.invalid", CancellationToken.None));
    }
}
