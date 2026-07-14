using System.Net;
using System.Net.Sockets;
using Hiram.Application.Delivery;

namespace Hiram.Infrastructure.Delivery;

// Resolves an SMTP host and refuses it when any resolved address is internal or reserved. Every resolved
// address is checked, not just the first, so a name that answers with both a public and a private IP (a
// rebinding attempt) is still blocked. The check runs again at connect time in the provider, so a name
// that flips to a private IP after it was accepted is caught on the next send, not only at write time.
public sealed class SmtpDestinationPolicy : ISmtpDestinationPolicy
{
    private static readonly TimeSpan ResolveTimeout = TimeSpan.FromSeconds(3);

    public async Task<SmtpDestinationVerdict> InspectAsync(string host, CancellationToken cancellationToken)
    {
        IPAddress[] addresses;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ResolveTimeout);
            addresses = await Dns.GetHostAddressesAsync(host, timeout.Token);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            // NXDOMAIN or a slow resolver is not a verdict we can trust either way, so let the caller
            // retry instead of permanently rejecting a name that may resolve on the next attempt.
            return SmtpDestinationVerdict.Unresolved;
        }

        if (addresses.Length == 0)
            return SmtpDestinationVerdict.Unresolved;

        return addresses.Any(IsInternal) ? SmtpDestinationVerdict.Blocked : SmtpDestinationVerdict.Allowed;
    }

    private static bool IsInternal(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (IPAddress.IsLoopback(address))
            return true;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] == 10                                   // 10.0.0.0/8 private
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)    // 172.16.0.0/12 private
                || (b[0] == 192 && b[1] == 168)                 // 192.168.0.0/16 private
                || (b[0] == 169 && b[1] == 254)                 // 169.254.0.0/16 link-local, incl. Azure metadata
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)   // 100.64.0.0/10 carrier-grade NAT
                || b[0] == 0                                    // 0.0.0.0/8 "this network"
                || b[0] >= 224;                                 // 224.0.0.0/4 multicast and 240/4 reserved
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast)
                return true;

            // fc00::/7 unique local addresses.
            return (address.GetAddressBytes()[0] & 0xFE) == 0xFC;
        }

        // An address family we do not recognise is refused rather than trusted.
        return true;
    }
}
