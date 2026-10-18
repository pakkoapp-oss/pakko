using System.Diagnostics;
using System.IO.Compression;
using FluentAssertions;

namespace Archiver.CLI.Tests.Adversarial;

/// <summary>
/// T-F419 red team: 'r' (writes a copy, never overwrites), 't' over recovery data, and -si/-so.
/// </summary>
public sealed class RepairAndStreamAdversarialTests : IDisposable
{
    private readonly AdversarialScratch _scratch = new();

    public void Dispose() => _scratch.Dispose();

    // A ZIP with a 10 % PAR2 set next to it, written by pakko itself, then damaged in its data.
    private string DamagedArchiveWithRecoveryData()
    {
        byte[] data = new byte[50_000];
        new Random(419).NextBytes(data);
        File.WriteAllBytes(_scratch.Inside("b.bin"), data);
        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "-rr10", "o.zip", "b.bin");
        exitCode.Should().Be(0, stdErr);
        string archive = _scratch.Inside("o.zip");
        byte[] bytes = File.ReadAllBytes(archive);
        Array.Clear(bytes, 1000, 16);
        File.WriteAllBytes(archive, bytes);
        return archive;
    }

    [Fact]
    public void Repair_TakenRepairedName_GetsANumberAndTheUserFileIsKept()
    {
        string archive = DamagedArchiveWithRecoveryData();
        byte[] damaged = File.ReadAllBytes(archive);
        string userFile = _scratch.WriteFile("o.repaired.zip", "USER FILE");

        (int exitCode, string stdOut, string stdErr) = _scratch.Pakko("r", "o.zip", "-y");

        exitCode.Should().Be(0, stdErr);
        File.ReadAllText(userFile).Should().Be("USER FILE");
        File.ReadAllBytes(archive).Should().Equal(damaged, "r only reads the archive");
        stdOut.Should().Contain("o.repaired (1).zip");
        using ZipArchive repaired = ZipFile.OpenRead(_scratch.Inside("o.repaired (1).zip"));
        repaired.Entries.Should().ContainSingle(e => e.FullName == "b.bin");
    }

    [Fact]
    public void Repair_OutputIsAFile_IsExitTwoAndTheFileIsKept()
    {
        DamagedArchiveWithRecoveryData();
        string file = _scratch.WriteFile("not-a-folder", "KEEP");

        (int exitCode, _, _) = _scratch.Pakko("r", "o.zip", "-onot-a-folder");

        exitCode.Should().Be(2);
        File.ReadAllText(file).Should().Be("KEEP");
    }

    [Fact]
    public void Repair_OutputFolderMissing_IsCreatedAndTheCopyLandsThere()
    {
        DamagedArchiveWithRecoveryData();

        (int exitCode, _, string stdErr) = _scratch.Pakko("r", "o.zip", @"-onew\deeper");

        exitCode.Should().Be(0, stdErr);
        File.Exists(_scratch.Inside(@"new\deeper\o.repaired.zip")).Should().BeTrue();
    }

    [Theory]
    [InlineData("-so")]
    [InlineData("-si")]
    [InlineData("-psecret")]
    [InlineData("-aoa")]
    [InlineData("-r")]
    [InlineData("-x!b.bin")]
    public void Repair_RefusedSwitch_IsExitSevenAndWritesNothing(string refused)
    {
        DamagedArchiveWithRecoveryData();
        int before = Directory.EnumerateFileSystemEntries(_scratch.Root).Count();

        (int exitCode, _, string stdErr) = _scratch.Pakko("r", "o.zip", refused);

        exitCode.Should().Be(7, stdErr);
        stdErr.Should().NotContain("secret");
        Directory.EnumerateFileSystemEntries(_scratch.Root).Count().Should().Be(before);
    }

    [Fact]
    public void Repair_JunkPar2_IsExitTwoAndWritesNoCopy()
    {
        _scratch.Zip("o.zip", ("a.txt", "A"));
        _scratch.WriteFile("o.zip.par2", "NOT A PAR2 FILE");

        (int exitCode, _, _) = _scratch.Pakko("r", "o.zip");

        exitCode.Should().Be(2);
        File.Exists(_scratch.Inside("o.repaired.zip")).Should().BeFalse();
    }

    [Fact]
    public void Test_DamagedButRepairable_IsExitTwoAndChangesNothing()
    {
        string archive = DamagedArchiveWithRecoveryData();
        byte[] damaged = File.ReadAllBytes(archive);
        int before = Directory.EnumerateFileSystemEntries(_scratch.Root).Count();

        (int exitCode, _, string stdErr) = _scratch.Pakko("t", "o.zip");

        exitCode.Should().Be(2, stdErr);
        File.ReadAllBytes(archive).Should().Equal(damaged);
        Directory.EnumerateFileSystemEntries(_scratch.Root).Count().Should().Be(before);
    }

    // --- -si / -so ---

    [Fact]
    public void ExtractFromStdin_Garbage_IsExitTwoAndCreatesNoOutputFolder()
    {
        (int exitCode, _, _) = RunWithStdin("NOT A ZIP AT ALL"u8.ToArray(), "x", "-si", "-oout");

        exitCode.Should().Be(2);
        Directory.Exists(_scratch.Inside("out")).Should().BeFalse();
    }

    [Fact]
    public void ExtractFromStdin_Empty_IsExitTwo()
    {
        (int exitCode, _, string stdErr) = RunWithStdin([], "x", "-si", "-oout");

        exitCode.Should().Be(2);
        stdErr.Should().Contain("stdin was empty");
    }

    [Fact]
    public void ExtractFromStdin_WithAoa_OverwritesOnlyWhatTheArchiveHolds()
    {
        string archive = _scratch.Zip(@"in\arc.zip", ("a.txt", "NEW"));
        string other = _scratch.WriteFile(@"out\other.txt", "PRECIOUS");
        _scratch.WriteFile(@"out\a.txt", "OLD");

        (int exitCode, _, string stdErr) = RunWithStdin(File.ReadAllBytes(archive), "x", "-si", "-oout", "-aoa");

        exitCode.Should().Be(0, stdErr);
        File.ReadAllText(_scratch.Inside(@"out\a.txt")).Should().Be("NEW");
        File.ReadAllText(other).Should().Be("PRECIOUS");
    }

    [Fact]
    public void ExtractFromStdin_BarePassword_IsExitSeven()
    {
        string archive = _scratch.Zip(@"in\arc.zip", ("a.txt", "NEW"));

        (int exitCode, _, _) = RunWithStdin(File.ReadAllBytes(archive), "x", "-si", "-p", "-oout");

        exitCode.Should().Be(7);
        Directory.Exists(_scratch.Inside("out")).Should().BeFalse();
    }

    [Fact]
    public void ArchiveToStdout_ThenExtractFromStdin_RoundTripsTheBytes()
    {
        byte[] data = new byte[200_000];
        new Random(7).NextBytes(data);
        File.WriteAllBytes(_scratch.Inside("big.bin"), data);

        (int packCode, byte[] zip, string packErr) = RunWithStdin([], "a", "-so", "x.zip", "big.bin");
        packCode.Should().Be(0, packErr);
        File.Exists(_scratch.Inside("x.zip")).Should().BeFalse("-so writes no archive file");

        (int unpackCode, _, string unpackErr) = RunWithStdin(zip, "x", "-si", "-oout");
        unpackCode.Should().Be(0, unpackErr);
        File.ReadAllBytes(_scratch.Inside(@"out\big.bin")).Should().Equal(data);
    }

    [Fact]
    public void ExtractToStdout_WithAoa_CurrentFolderIsNotTouched()
    {
        string archive = _scratch.Zip("arc.zip", ("a.txt", "NEW"));
        string existing = _scratch.WriteFile("a.txt", "PRECIOUS");

        (int exitCode, byte[] stdOut, string stdErr) = RunWithStdin([], "x", archive, "-so", "-aoa");

        exitCode.Should().Be(0, stdErr);
        stdOut.Should().Equal("NEW"u8.ToArray());
        File.ReadAllText(existing).Should().Be("PRECIOUS", "-so writes to stdout, not into the current folder");
    }

    [Fact]
    public void HashFromStdin_WithAPath_IsExitSeven()
    {
        _scratch.WriteFile("b.txt", "B");

        (int exitCode, _, _) = RunWithStdin("x"u8.ToArray(), "h", "-si", "b.txt");

        exitCode.Should().Be(7);
    }

    private (int ExitCode, byte[] StdOut, string StdErr) RunWithStdin(byte[] stdin, params string[] args)
    {
        var startInfo = new ProcessStartInfo(Subprocess.CliProcessRunner.ExePath)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = _scratch.Root,
        };
        foreach (string arg in args)
            startInfo.ArgumentList.Add(arg);
        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("pakko did not start");
        using var stdOut = new MemoryStream();
        Task copy = process.StandardOutput.BaseStream.CopyToAsync(stdOut);
        Task<string> stdErr = process.StandardError.ReadToEndAsync();
        process.StandardInput.BaseStream.Write(stdin);
        process.StandardInput.Close();
        copy.GetAwaiter().GetResult();
        string errors = stdErr.GetAwaiter().GetResult();
        process.WaitForExit();
        return (process.ExitCode, stdOut.ToArray(), errors);
    }
}
