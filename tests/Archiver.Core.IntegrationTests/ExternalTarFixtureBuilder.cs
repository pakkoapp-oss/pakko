using System.Diagnostics;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// Creates compressed tar archives (tar.gz/tar.bz2/tar.xz/tar.zst/tar.lzma) by shelling out to
/// the real system tar.exe, rather than committing a static binary corpus (T-F50). tar.exe can
/// create these formats itself (empirically confirmed - see DECISIONS.md's T-F50 entry), unlike
/// 7z/rar, which tar.exe can only read, never write. Writes source entries to a temp directory
/// first since tar.exe compresses from real files, not from an in-memory stream.
/// </summary>
internal static class ExternalTarFixtureBuilder
{
    private const string TarExecutablePath = @"C:\Windows\System32\tar.exe";

    public static void CreateCompressedTar(
        string destArchivePath,
        string compressionFlag,
        IEnumerable<(string Name, string Content)> entries)
    {
        using var sourceDir = new TempDirectory();
        var names = new List<string>();
        foreach (var (name, content) in entries)
        {
            string fullPath = Path.Combine(sourceDir.Path, name);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content, System.Text.Encoding.UTF8);
            names.Add(name);
        }

        var args = new List<string>();
        args.AddRange(compressionFlag.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        args.Add(destArchivePath);
        args.AddRange(names);

        var startInfo = new ProcessStartInfo
        {
            FileName = TarExecutablePath,
            WorkingDirectory = sourceDir.Path,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start tar.exe");
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"tar.exe exited {process.ExitCode}: {stderr}");
    }
}
