namespace Hiram.Application.Tenancy;

// Protects tenant provider secrets at rest. The implementation lives in Infrastructure (ASP.NET Data
// Protection); the Application only depends on this boundary.
public interface ISecretProtector
{
    string Protect(string plaintext);

    string Unprotect(string protectedValue);
}
