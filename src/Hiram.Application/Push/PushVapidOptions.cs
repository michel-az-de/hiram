namespace Hiram.Application.Push;

// Platform VAPID identity. The private key is a secret and never leaves the server or reaches a log.
public sealed record PushVapidOptions(string Subject, string PublicKey, string PrivateKey);
