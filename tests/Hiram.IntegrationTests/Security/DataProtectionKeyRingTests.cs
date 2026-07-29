using System.Diagnostics;

namespace Hiram.IntegrationTests.Security;

public sealed class DataProtectionKeyRingTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "hiram-dp-" + Guid.NewGuid().ToString("N"));

    public DataProtectionKeyRingTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task HostReplicaEncrypts_AnotherReplicaDecrypts_AcrossProcesses()
    {
        var sharedKeyRing = CreateDir("keyring");
        var firstProfile = CreateDir("first-profile");
        var secondProfile = CreateDir("second-profile");
        const string secret = "tenant-smtp-password-42";

        var protect = await RunProbeAsync(firstProfile, "protect", sharedKeyRing, secret);
        Assert.True(protect.ExitCode == 0, $"protect process failed: {protect.StdErr}");
        var ciphertext = protect.StdOut.Trim();
        Assert.NotEqual(secret, ciphertext);

        var unprotect = await RunProbeAsync(secondProfile, "unprotect", sharedKeyRing, ciphertext);
        Assert.True(
            unprotect.ExitCode == 0,
            $"second host process could not decrypt the ciphertext: {unprotect.StdErr}");
        Assert.Equal(secret, unprotect.StdOut.Trim());
    }

    private string CreateDir(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task<ProbeResult> RunProbeAsync(string profileDir, params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("exec");
        start.ArgumentList.Add(ProbeAssemblyPath());
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        // Isolate each process default key store so the only path they can share is the explicit
        // key ring. This mirrors two containers with separate local filesystems and one shared
        // volume, which is exactly where the missing shared key ring bites in production.
        foreach (var variable in new[] { "LOCALAPPDATA", "APPDATA", "USERPROFILE", "HOME", "XDG_DATA_HOME" })
            start.Environment[variable] = profileDir;

        using var process = Process.Start(start)!;
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProbeResult(process.ExitCode, stdout, stderr);
    }

    private static string ProbeAssemblyPath()
    {
        var tfmDir = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        var configuration = tfmDir.Parent!.Name;
        var testsDir = tfmDir.Parent!.Parent!.Parent!.Parent!.FullName;
        return Path.Combine(
            testsDir,
            "Hiram.DataProtectionProbe",
            "bin",
            configuration,
            tfmDir.Name,
            "Hiram.DataProtectionProbe.dll");
    }

    public void Dispose()
    {
        // Best effort: a leftover temp key ring is harmless if the OS still holds a handle.
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    private sealed record ProbeResult(int ExitCode, string StdOut, string StdErr);
}
