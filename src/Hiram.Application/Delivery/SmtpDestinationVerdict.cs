namespace Hiram.Application.Delivery;

// The result of checking whether a tenant-configured SMTP host may be dialed. Blocked means it resolved
// to an internal or reserved address (an SSRF attempt); Unresolved means DNS did not answer in time, so
// the caller should treat it as transient and retry rather than reject the config outright.
public enum SmtpDestinationVerdict
{
    Allowed,
    Blocked,
    Unresolved
}
