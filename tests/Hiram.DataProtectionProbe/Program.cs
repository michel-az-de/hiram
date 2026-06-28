using System.Security.Cryptography;
using Hiram.Application.Tenancy;
using Hiram.Infrastructure;
using Hiram.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;

// Cross-process harness for the shared Data Protection key ring. One invocation protects,
// a separate invocation unprotects, so the test can prove the dispatcher decrypts what the
// api encrypted. It exercises the real AddHiramDataProtection registration and the real
// DataProtectionSecretProtector, never an in-proc reimplementation.
if (args.Length != 3)
{
    Console.Error.WriteLine("usage: <protect|unprotect> <keyRingPath> <value>");
    return 2;
}

var (command, keyRingPath, value) = (args[0], args[1], args[2]);

var services = new ServiceCollection();
services.AddHiramDataProtection(keyRingPath);
services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();
using var provider = services.BuildServiceProvider();
var protector = provider.GetRequiredService<ISecretProtector>();

try
{
    switch (command)
    {
        case "protect":
            Console.Out.Write(protector.Protect(value));
            return 0;
        case "unprotect":
            Console.Out.Write(protector.Unprotect(value));
            return 0;
        default:
            Console.Error.WriteLine($"unknown command: {command}");
            return 2;
    }
}
catch (CryptographicException ex)
{
    // Decrypt failure is the bug we are testing for: surface it as a non-zero exit so the
    // parent process can assert on it instead of the exception being swallowed.
    Console.Error.WriteLine($"CryptographicException: {ex.Message}");
    return 1;
}
