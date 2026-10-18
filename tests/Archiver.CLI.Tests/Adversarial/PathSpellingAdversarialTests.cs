using System.Diagnostics;
using FluentAssertions;

namespace Archiver.CLI.Tests.Adversarial;

/// <summary>
/// T-F419 red team: the same archive named every way Windows accepts, and tar.exe's own collisions.
/// </summary>
public sealed class PathSpellingAdversarialTests : IDisposable
{
    private const string TarExe = @"C:\Windows\System32\tar.exe";
    private readonly AdversarialScratch _scratch = new();

    public PathSpellingAdversarialTests()
    {
        _scratch.Zip("archivefile.zip", ("a.txt", "A"));
    }

    public void Dispose() => _scratch.Dispose();

    // 7za 26.02 opens \\?\C:\...\arc.zip (exit 0). Pakko reads the '?' as a wildcard and says
    // "Cannot find archive", exit 7, for a file that is there; -spd (wildcards off) opens it.
    [Theory]
    [InlineData("x")]
    [InlineData("t")]
    [InlineData("l")]
    public void LongPathPrefixArchive_IsOpened(string command)
    {
        string archive = @"\\?\" + _scratch.Inside("archivefile.zip");
        string[] args = command == "x" ? [command, archive, "-oout"] : [command, archive];

        (int exitCode, _, string stdErr) = _scratch.Pakko(args);

        exitCode.Should().Be(0, $"the archive exists; 7za opens this spelling. stderr: {stdErr}");
    }

    [Theory]
    [InlineData(@".\archivefile.zip")]
    [InlineData("./archivefile.zip")]
    [InlineData("ARCHIVEFILE.ZIP")]
    [InlineData(@"sub\..\archivefile.zip")]
    [InlineData("ARCHIV~1.ZIP")]
    public void ArchiveSpelledAnotherWay_IsOpened(string spelling)
    {
        _scratch.Folder("sub");

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", spelling, "-oout");

        exitCode.Should().Be(0, stdErr);
        File.ReadAllText(_scratch.Inside(@"out\a.txt")).Should().Be("A");
    }

    [Fact]
    public void LongPathPrefixOutput_IsUsed()
    {
        (int exitCode, _, string stdErr) = _scratch.Pakko("x", "archivefile.zip", @"-o\\?\" + _scratch.Inside("out"));

        exitCode.Should().Be(0, stdErr);
        File.ReadAllText(_scratch.Inside(@"out\a.txt")).Should().Be("A");
    }

    [Fact]
    public void UncArchiveAndOutput_Work()
    {
        string unc = @"\\localhost\" + _scratch.Root[0] + "$" + _scratch.Root[2..];
        if (!Directory.Exists(unc))
            return; // the administrative share is not reachable on this machine; the report says so

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", unc + @"\archivefile.zip", $@"-o{unc}\out", "-aoa");

        exitCode.Should().Be(0, stdErr);
        File.ReadAllText(_scratch.Inside(@"out\a.txt")).Should().Be("A");
    }

    [Fact]
    public void AouNumbering_SkipsANumberThatIsTaken()
    {
        string first = _scratch.WriteFile(@"out\a.txt", "OLD0");
        string second = _scratch.WriteFile(@"out\a (1).txt", "OLD1");

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", "archivefile.zip", "-oout", "-aou");

        exitCode.Should().Be(0, stdErr);
        File.ReadAllText(first).Should().Be("OLD0");
        File.ReadAllText(second).Should().Be("OLD1");
        File.ReadAllText(_scratch.Inside(@"out\a (2).txt")).Should().Be("A");
    }

    // A tar holding a.txt and A.TXT: tar.exe would write both into one file on NTFS.
    [Theory]
    [InlineData(null, 1)]
    [InlineData("-aos", 1)]
    [InlineData("-aou", 0)]
    public void TarCaseTwin_IsNotSilentlyFolded(string? overwrite, int expectedExit)
    {
        _scratch.WriteFile(@"d1\a.txt", "lower");
        _scratch.WriteFile(@"d2\A.TXT", "UPPER");
        RunTar(_scratch.Inside("d1"), "-cf", _scratch.Inside("t1.tar"), "a.txt");
        RunTar(_scratch.Inside("d2"), "-cf", _scratch.Inside("t2.tar"), "A.TXT");
        RunTar(_scratch.Root, "-czf", _scratch.Inside("both.tar.gz"), "@t1.tar", "@t2.tar");
        string[] args = overwrite is null ? ["x", "both.tar.gz", "-oout"] : ["x", "both.tar.gz", "-oout", overwrite];

        (int exitCode, _, string stdErr) = _scratch.Pakko(args);

        exitCode.Should().Be(expectedExit, stdErr);
        string[] contents = [.. _scratch.FilesUnder("out").Select(name => File.ReadAllText(_scratch.Inside(Path.Combine("out", name))))];
        contents.Should().Contain("lower");
        if (overwrite == "-aou")
            contents.Should().Contain("UPPER");
    }

    private static void RunTar(string workingDirectory, params string[] args)
    {
        var startInfo = new ProcessStartInfo(TarExe)
        {
            WorkingDirectory = workingDirectory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardError = true, RedirectStandardOutput = true,
        };
        foreach (string arg in args)
            startInfo.ArgumentList.Add(arg);
        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("tar.exe did not start");
        process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        process.ExitCode.Should().Be(0, $"tar.exe builds the fixture: {error}");
    }
}
