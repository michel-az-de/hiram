namespace Hiram.Application.Delivery;

// Guards tenant-configured SMTP hosts against pointing the sender at internal infrastructure (cloud
// metadata endpoints, databases, loopback). Lives behind a port because it resolves DNS, which is I/O the
// Application layer must not do directly.
public interface ISmtpDestinationPolicy
{
    Task<SmtpDestinationVerdict> InspectAsync(string host, CancellationToken cancellationToken);
}
