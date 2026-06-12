namespace Hiram.Application.Delivery;

public sealed record ResolvedEmailProvider(IEmailProvider Provider, EmailProviderSettings Settings);
