using System.Diagnostics;
using System.IO.Compression;

namespace Archiver.CLI.Tests.Subprocess;

/// <summary>
/// Builds real archive fixtures once for the whole subprocess test run — a ZIP built inline via
/// ZipFile.CreateFromDirectory (no new dependency) and, when tar.exe is present, a .tar.gz built
/// by shelling directly to C:\Windows\System32\tar.exe (absolute path, per this repo's hard
/// constraint). Reuses fixture-building technique already used elsewhere in this repo
/// (Archiver.Core.IntegrationTests' TarBuilder/ExternalTarFixtureBuilder), not the fixture files
/// themselves — this project builds its own so it stays decoupled from that test project.
/// </summary>
internal static class CliFixtureFiles
{
    public static string SourceDir { get; }
    public static string SourceFileA { get; }
    public static string SourceFileB { get; }
    public static string ValidZip { get; }
    public static string? ValidTarGz { get; }
    public static string LargeSourceFile { get; }

    static CliFixtureFiles()
    {
        string root = Path.Combine(Path.GetTempPath(), "Archiver.CLI.Tests.Fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        SourceDir = Path.Combine(root, "source");
        Directory.CreateDirectory(SourceDir);
        SourceFileA = Path.Combine(SourceDir, "a.txt");
        SourceFileB = Path.Combine(SourceDir, "b.txt");
        File.WriteAllText(SourceFileA, "hello world");
        File.WriteAllText(SourceFileB, "second file");

        ValidZip = Path.Combine(root, "valid.zip");
        ZipFile.CreateFromDirectory(SourceDir, ValidZip);

        ValidTarGz = File.Exists(@"C:\Windows\System32\tar.exe")
            ? BuildTarGz(root, SourceDir)
            : null;

        // T-F116: deliberately large (well past the 81920-byte CopyToAsync buffer size in
        // CliStreamStaging) and poorly compressible (random bytes, fixed seed for determinism) —
        // the broken-downstream-pipe test needs the -so stream copy to span multiple internal
        // write() calls so an early-closed reader is actually observed mid-stream, not before the
        // single write for a small payload has already completed.
        LargeSourceFile = Path.Combine(root, "large.bin");
        byte[] largeData = new byte[5 * 1024 * 1024];
        new Random(42).NextBytes(largeData);
        File.WriteAllBytes(LargeSourceFile, largeData);

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best-effort cleanup */ }
        };
    }

    private static string BuildTarGz(string root, string sourceDir)
    {
        string tarGzPath = Path.Combine(root, "valid.tar.gz");
        var startInfo = new ProcessStartInfo(@"C:\Windows\System32\tar.exe")
        {
            WorkingDirectory = sourceDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-czf");
        startInfo.ArgumentList.Add(tarGzPath);
        startInfo.ArgumentList.Add("a.txt");
        startInfo.ArgumentList.Add("b.txt");

        using Process process = Process.Start(startInfo)!;
        process.WaitForExit();
        return tarGzPath;
    }

    public static string CreateScratchDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "Archiver.CLI.Tests.Out", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
