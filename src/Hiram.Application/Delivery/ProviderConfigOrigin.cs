namespace Hiram.Application.Delivery;

// Where an email provider config came from. The tenant path is untrusted and gets guarded against SSRF
// and forced onto TLS; the platform path is the operator's and may target an internal MTA. Required at
// every construction of EmailProviderSettings so a new call site cannot silently fall open to the tenant
// rules being skipped.
public enum ProviderConfigOrigin
{
    Platform,
    Tenant
}
