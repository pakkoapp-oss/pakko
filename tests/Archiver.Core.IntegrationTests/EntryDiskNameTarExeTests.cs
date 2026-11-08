using System.Diagnostics;
using System.Formats.Tar;
using System.Text;
using Archiver.Core.Services;
using Archiver.Core.Services.Sandbox;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

// T-F420: EntryDiskName.AsTarExeWrites copies what libarchive does to a name on Windows; real
// tar.exe is the oracle, so an update to it that writes names differently fails here, not in a
// user's extraction.
public sealed class EntryDiskNameTarExeTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Integration]
    public void AsTarExeWrites_IsWhatTarExeWrites()
    {
        string[] names = ["a?b.txt", "x.txt ", "a.txt.", "d./x.txt", "d?/x|y.txt", "p<q>/r*s\".txt"];
        var mismatches = new List<string>();
        for (int i = 0; i < names.Length; i++)
        {
            string archive = Path.Combine(_temp.Path, i + ".tar");
            using (FileStream file = File.Create(archive))
            using (var writer = new TarWriter(file, TarEntryFormat.Pax))
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, names[i]) { DataStream = new MemoryStream(Encoding.UTF8.GetBytes("x")) });
            string output = Path.Combine(_temp.Path, i.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Directory.CreateDirectory(output);
            RunTar(archive, output);

            string[] written = [.. Directory.EnumerateFiles(@"\\?\" + output, "*", SearchOption.AllDirectories)
                .Select(p => Path.GetRelativePath(@"\\?\" + output, p).Replace('\\', '/'))];
            string expected = EntryDiskName.AsTarExeWrites(names[i]);
            if (written.Length != 1 || written[0] != expected)
                mismatches.Add($"'{names[i]}': predicted '{expected}', tar.exe wrote [{string.Join(", ", written.Select(w => "'" + w + "'"))}]");
        }

        mismatches.Should().BeEmpty();
    }

    // T-F454: with the folders Pakko makes before tar.exe runs (T-F52), a folder ending in a space
    // or a dot is where tar.exe writes the file, and its name is the one Pakko predicts.
    [Integration]
    public void AsTarExeWrites_WithPakkosPreCreatedFolders_IsWhatTarExeWrites()
    {
        string[] names = ["d /x.txt", "d. /x.txt", "d ./x.txt", "a/d /x.txt", "d./x.txt"];
        var mismatches = new List<string>();
        for (int i = 0; i < names.Length; i++)
        {
            string archive = Path.Combine(_temp.Path, "pre" + i + ".tar");
            using (FileStream file = File.Create(archive))
            using (var writer = new TarWriter(file, TarEntryFormat.Pax))
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, names[i]) { DataStream = new MemoryStream(Encoding.UTF8.GetBytes("x")) });
            string output = Path.Combine(_temp.Path, "pre" + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Directory.CreateDirectory(output);
            TarExeReader.PreCreateImplicitFolders([names[i]], output);
            if (!TryRunTar(archive, output, out string stdErr))
            {
                mismatches.Add($"'{names[i]}': tar.exe failed: {stdErr}");
                continue;
            }

            string[] written = [.. Directory.EnumerateFiles(@"\\?\" + output, "*", SearchOption.AllDirectories)
                .Select(p => Path.GetRelativePath(@"\\?\" + output, p).Replace('\\', '/'))];
            string expected = EntryDiskName.AsTarExeWrites(names[i]);
            if (written.Length != 1 || written[0] != expected)
                mismatches.Add($"'{names[i]}': predicted '{expected}', tar.exe wrote [{string.Join(", ", written.Select(w => "'" + w + "'"))}]");
        }

        mismatches.Should().BeEmpty();
    }

    private static void RunTar(string archive, string output) =>
        TryRunTar(archive, output, out string stdErr).Should().BeTrue(stdErr);

    private static bool TryRunTar(string archive, string output, out string stdErr)
    {
        var startInfo = new ProcessStartInfo(@"C:\Windows\System32\tar.exe")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true,
        };
        foreach (string arg in new[] { "-xf", archive, "-C", output })
            startInfo.ArgumentList.Add(arg);
        using Process tar = Process.Start(startInfo) ?? throw new InvalidOperationException("tar.exe did not start");
        stdErr = tar.StandardError.ReadToEnd();
        tar.StandardOutput.ReadToEnd();
        tar.WaitForExit();
        return tar.ExitCode == 0;
    }
}
