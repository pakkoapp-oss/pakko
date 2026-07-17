using System.Diagnostics;
using System.Runtime.InteropServices;
using Archiver.Core.Services.Sandbox;

namespace Archiver.Core.PerformanceTests;

/// <summary>
/// T-F114: runs the vendored, test-only <c>7za.exe</c> reference binary (see
/// Tools/7-Zip/NOTICE.md) as an external process and times it — the reference side of the
/// same-machine, same-invocation ratio comparison the performance tests assert on. Never
/// resolved via PATH or a system install, mirroring tar.exe's existing absolute-path-only
/// convention (CLAUDE.md).
///
/// Launched under a basic sandbox — <see cref="SandboxJobObject"/> (Job Object: no child-process
/// creation, RAM/CPU caps) via the same <see cref="SandboxedProcessLauncher"/> tar.exe already
/// uses, but deliberately WITHOUT the AppContainer/quarantine-staging layer (attributeList: null)
/// — that layer exists to contain untrusted *input* (see TarSandboxScope), which doesn't apply
/// here since the fixture is Pakko's own generated content, not attacker-controlled. The Job
/// Object alone mitigates the risk that matters for this binary specifically — it being a
/// third-party executable that could theoretically be compromised despite the hash verification
/// in NOTICE.md — without the ACL/staging overhead that would bias the very timing being
/// measured. See DECISIONS.md's T-F114 entry and SECURITY.md for the full rationale.
/// </summary>
public static class SevenZipRunner
{
    // Generous headroom, not a tight security boundary — the point is bounding a genuinely
    // runaway/malicious process, not constraining legitimate 7za memory/CPU use on a 300 MB file.
    private static readonly long RamLimitBytes = 2L * 1024 * 1024 * 1024;
    private static readonly TimeSpan CpuTimeLimit = TimeSpan.FromMinutes(10);

    private static readonly string ExePath = ResolveExePath();

    // Defense-in-depth only (e.g. someone deleted the vendored file) — the routine case is that
    // ExePath always exists, since it's committed to the repo and copied to output on every build.
    public static bool IsAvailable => File.Exists(ExePath);

    public static TimeSpan Archive(string sourceDir, string destinationZipPath) =>
        Run(["a", "-tzip", "-mx=5", "-bd", destinationZipPath, sourceDir]);

    public static TimeSpan Extract(string archivePath, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);
        return Run(["x", archivePath, $"-o{destinationDir}", "-y", "-bd"]);
    }

    /// <summary>
    /// Runs 7-Zip's own integrity check (<c>t</c>) against an archive — used by T-F35's
    /// <c>ZipEntryWriter</c> compatibility tests as an independent, strict, third-party ZIP
    /// reader to validate hand-rolled container bytes against (not a timing measurement; the
    /// elapsed time is discarded). Throws if 7-Zip reports the archive as invalid/corrupt.
    /// </summary>
    public static void Test(string archivePath) => Run(["t", archivePath, "-bd"]);

    private static TimeSpan Run(IReadOnlyList<string> arguments)
    {
        if (!IsAvailable)
            throw new InvalidOperationException(
                $"Vendored 7za.exe not found at '{ExePath}' — see Tools/7-Zip/NOTICE.md. This " +
                "should only happen if the file was deleted or excluded from the build output.");

        using SandboxJobObject job = SandboxJobObject.Create(RamLimitBytes, CpuTimeLimit);

        var stopwatch = Stopwatch.StartNew();
        (int exitCode, string _, string stdErr) = SandboxedProcessLauncher.RunAsync(
                ExePath, arguments, attributeList: null, job.Handle, CancellationToken.None)
            .GetAwaiter().GetResult();
        stopwatch.Stop();

        if (exitCode != 0)
            throw new InvalidOperationException($"7za.exe exited with code {exitCode}: {stdErr}");

        return stopwatch.Elapsed;
    }

    private static string ResolveExePath()
    {
        string arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        return Path.Combine(AppContext.BaseDirectory, "Tools", "7-Zip", arch, "7za.exe");
    }
}
