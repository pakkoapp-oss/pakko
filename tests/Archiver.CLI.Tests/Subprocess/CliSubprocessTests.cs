using System.IO.Compression;
using FluentAssertions;

namespace Archiver.CLI.Tests.Subprocess;

/// <summary>
/// Real Process.Start of the built pakko.exe against real archive fixtures, asserting real
/// exit codes and real stdout/stderr text — genuinely new to this repo (TASKS.md's T-F09):
/// Archiver.Shell's args are only ever generated programmatically by the shell extension, so
/// unit-testing its parser class in isolation is sufficient there. A human or script types
/// Archiver.CLI's arguments directly, so its exit code and stdout/stderr text ARE the public
/// contract, not an implementation detail — a parser-only suite would never catch a real process
/// returning the wrong exit code or malformed output.
/// </summary>
public sealed class CliSubprocessTests
{
    // --- x: happy path ---

    [Fact]
    public void Extract_ZipHappyPath_ExtractsFilesAndExitsZero()
    {
        string destDir = CliFixtureFiles.CreateScratchDir();

        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("x", $"-o{destDir}", CliFixtureFiles.ValidZip);

        exitCode.Should().Be(0);
        stdErr.Should().BeEmpty();
        // Two root-level files with no common containing folder → Core's existing smart-foldering
        // wraps them in a subfolder named after the archive (same mechanism Archiver.Shell's own
        // "extract flat" command already relies on) — not a CLI-specific behavior.
        string extractedFile = Path.Combine(destDir, "valid", "a.txt");
        File.Exists(extractedFile).Should().BeTrue();
        File.ReadAllText(extractedFile).Should().Be("hello world");
    }

    [RequiresTarExe]
    public void Extract_TarGzHappyPath_ExtractsFilesAndExitsZero()
    {
        string destDir = CliFixtureFiles.CreateScratchDir();

        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("x", $"-o{destDir}", CliFixtureFiles.ValidTarGz!);

        exitCode.Should().Be(0);
        stdErr.Should().BeEmpty();
        // T-F118: TarSandboxedService's extraction now shares ZipArchiveService's smart-foldering
        // exactly — two root-level files with no common containing folder wrap in a subfolder
        // named after the archive, same as the ZIP fixture above (this fixture's SourceDir is the
        // same a.txt/b.txt shape).
        File.Exists(Path.Combine(destDir, "valid", "a.txt")).Should().BeTrue();
    }

    [RequiresTarCapability("7z")]
    public void Extract_SevenZipFixture_ExtractsFileWithContentAndExitsZero()
    {
        string destDir = CliFixtureFiles.CreateScratchDir();
        string sevenZipPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "valid.7z");

        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("x", $"-o{destDir}", sevenZipPath);

        exitCode.Should().Be(0);
        File.ReadAllText(Path.Combine(destDir, "seven.txt")).Should().Be("hello from a real 7z fixture\n");
    }

    [RequiresTarCapability("rar")]
    public void Extract_RarFixture_ExtractsFileWithContentAndExitsZero()
    {
        string destDir = CliFixtureFiles.CreateScratchDir();
        string rarPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "valid.rar");

        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("x", $"-o{destDir}", rarPath);

        exitCode.Should().Be(0);
        File.ReadAllText(Path.Combine(destDir, "rar.txt")).Should().Be("hello from a real rar fixture\n");
    }

    // --- t: happy path + tar-family skip ---

    [Fact]
    public void Test_ZipHappyPath_ExitsZero()
    {
        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("t", CliFixtureFiles.ValidZip);

        exitCode.Should().Be(0);
        stdErr.Should().BeEmpty();
    }

    [RequiresTarExe]
    public void Test_TarGzArchive_ExitsOneAndNamesReason()
    {
        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("t", CliFixtureFiles.ValidTarGz!);

        exitCode.Should().Be(1);
        stdErr.Should().Contain("tar-family archives have no test capability");
    }

    // --- i: happy path ---

    [Fact]
    public void Info_ExitsZeroAndMentionsZip()
    {
        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("i");

        exitCode.Should().Be(0);
        stdOut.Should().Contain("zip");
    }

    // --- a: happy path ---

    [Fact]
    public void Archive_ZipHappyPath_CreatesRealZipWithBothEntries()
    {
        string destDir = CliFixtureFiles.CreateScratchDir();
        string outputZip = Path.Combine(destDir, "out.zip");

        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run(
            "a", outputZip, CliFixtureFiles.SourceFileA, CliFixtureFiles.SourceFileB);

        exitCode.Should().Be(0);
        stdErr.Should().BeEmpty();
        File.Exists(outputZip).Should().BeTrue();

        using ZipArchive archive = ZipFile.OpenRead(outputZip);
        archive.Entries.Select(e => e.Name).Should().BeEquivalentTo("a.txt", "b.txt");
    }

    [RequiresTarExe]
    public void Archive_TarGzType_CreatesRealGzipCompressedTar()
    {
        string destDir = CliFixtureFiles.CreateScratchDir();
        string outputPath = Path.Combine(destDir, "out.tar.gz");

        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run(
            "a", "-ttar.gz", outputPath, CliFixtureFiles.SourceFileA, CliFixtureFiles.SourceFileB);

        exitCode.Should().Be(0);
        File.Exists(outputPath).Should().BeTrue();

        (int tarExitCode, string tarStdOut, _) = RunTarExe("-tzvf", outputPath);
        tarExitCode.Should().Be(0);
        tarStdOut.Should().Contain("a.txt").And.Contain("b.txt");
    }

    // --- l: happy path ---

    [Fact]
    public void List_ZipHappyPath_PrintsHeaderAndBothEntries()
    {
        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("l", CliFixtureFiles.ValidZip);

        exitCode.Should().Be(0);
        stdErr.Should().BeEmpty();
        stdOut.Should().Contain("Size\tCompressed\tCrc32\tModified\tType\tPath");
        stdOut.Should().Contain("a.txt");
        stdOut.Should().Contain("b.txt");
    }

    // --- three-way rule: one real instance of each category ---

    [Fact]
    public void UnknownCommand_ExitsSevenWithIncorrectCommandLineMessage()
    {
        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("q", "archive.zip");

        exitCode.Should().Be(7);
        stdErr.Should().Contain("Incorrect command line");
    }

    [Fact]
    public void DeliberatelyUnsupportedCommand_ExitsSevenNamingReason()
    {
        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("d", "archive.zip");

        exitCode.Should().Be(7);
        stdErr.Should().Contain("not supported by Pakko");
    }

    [Fact]
    public void UnsupportedSwitchOnSupportedCommand_ExitsSevenNamingSwitch()
    {
        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("x", "-psecret", "archive.zip");

        exitCode.Should().Be(7);
        stdErr.Should().Contain("encryption");
    }

    // --- T-F116: -si / -so streaming ---

    [Fact]
    public void ArchiveWithSo_ThenExtractWithSi_RoundTripsBytesExactly()
    {
        (int archiveExit, byte[] archiveBytes, string archiveErr) = CliProcessRunner.RunWithBinaryStdio(
            stdinBytes: [], "a", "-so", "out.zip", CliFixtureFiles.SourceFileA, CliFixtureFiles.SourceFileB);

        archiveExit.Should().Be(0);
        archiveErr.Should().BeEmpty();
        archiveBytes.Should().NotBeEmpty();

        string destDir = CliFixtureFiles.CreateScratchDir();
        (int extractExit, byte[] extractStdOut, string extractErr) = CliProcessRunner.RunWithBinaryStdio(
            archiveBytes, "x", "-si", $"-o{destDir}");

        extractExit.Should().Be(0);
        extractErr.Should().BeEmpty();
        extractStdOut.Should().BeEmpty();
        // Two root-level files, no common folder → smart-foldering wraps them in a subfolder
        // named after ArchiveNaming.GetBaseName(archivePath) — since -si's archivePath is the
        // staged temp file "stdin.bin", the wrapper folder is literally "stdin". A real, accepted
        // consequence of buffering -si through a private temp file (see DECISIONS.md's T-F116
        // entry), not a bug.
        File.ReadAllText(Path.Combine(destDir, "stdin", "a.txt")).Should().Be("hello world");
        File.ReadAllText(Path.Combine(destDir, "stdin", "b.txt")).Should().Be("second file");
    }

    [RequiresTarCapability("7z")]
    public void Extract_SevenZipFixtureWithSo_StreamsSingleFileToStdout()
    {
        string sevenZipPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "valid.7z");

        (int exitCode, byte[] stdOut, string stdErr) = CliProcessRunner.RunWithBinaryStdio(
            stdinBytes: [], "x", "-so", sevenZipPath);

        exitCode.Should().Be(0);
        stdErr.Should().BeEmpty();
        System.Text.Encoding.UTF8.GetString(stdOut).Should().Be("hello from a real 7z fixture\n");
    }

    [RequiresTarCapability("rar")]
    public void Extract_RarFixtureWithSo_StreamsSingleFileToStdout()
    {
        string rarPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "valid.rar");

        (int exitCode, byte[] stdOut, string stdErr) = CliProcessRunner.RunWithBinaryStdio(
            stdinBytes: [], "x", "-so", rarPath);

        exitCode.Should().Be(0);
        stdErr.Should().BeEmpty();
        System.Text.Encoding.UTF8.GetString(stdOut).Should().Be("hello from a real rar fixture\n");
    }

    [Fact]
    public void Extract_ZipWithSo_MultipleFiles_ExitsTwoNamingCount()
    {
        (int exitCode, byte[] stdOut, string stdErr) = CliProcessRunner.RunWithBinaryStdio(
            stdinBytes: [], "x", "-so", CliFixtureFiles.ValidZip);

        exitCode.Should().Be(2);
        stdErr.Should().Contain("found 2");
        stdOut.Should().BeEmpty();
    }

    // T-F116 originally found a genuinely unrecognized single archive path (empty file, or bytes
    // that match no known archive magic number) was silently treated as a no-op success by
    // ZipArchiveService.ExtractAsync's per-item IsZipFile/GetKnownArchiveReason gate (neither an
    // error nor a skipped-file entry was recorded when GetKnownArchiveReason returned null) —
    // confirmed by reproducing the identical exit-0 result against a real on-disk garbage .zip via
    // plain 'x' (no -si involved at all). That Core-level gap is now fixed by T-F117 (a real
    // ArchiveError is recorded), so these tests assert the new loud-error behavior instead. See
    // DECISIONS.md's T-F116/T-F117 entries.
    [Fact]
    public void Extract_SiWithEmptyStdin_ErrorsAsUnrecognizedArchive()
    {
        string destDir = CliFixtureFiles.CreateScratchDir();

        (int exitCode, byte[] stdOut, string stdErr) = CliProcessRunner.RunWithBinaryStdio(
            stdinBytes: [], "x", "-si", $"-o{destDir}");

        exitCode.Should().Be(2);
        stdErr.Should().Contain("File is not a recognized archive format");
        Directory.GetFiles(destDir, "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public void Extract_SiWithGarbageBytes_ErrorsAsUnrecognizedArchive()
    {
        string destDir = CliFixtureFiles.CreateScratchDir();
        byte[] garbage = System.Text.Encoding.UTF8.GetBytes("this is not an archive, just plain garbage text data");

        (int exitCode, byte[] stdOut, string stdErr) = CliProcessRunner.RunWithBinaryStdio(
            garbage, "x", "-si", $"-o{destDir}");

        exitCode.Should().Be(2);
        stdErr.Should().Contain("File is not a recognized archive format");
        Directory.GetFiles(destDir, "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public void Archive_SiSwitch_ExitsSevenNamingReason()
    {
        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run(
            "a", "-si", "out.zip", CliFixtureFiles.SourceFileA);

        exitCode.Should().Be(7);
        stdErr.Should().Contain("not supported on this command");
    }

    [Fact]
    public void CmdPipe_ArchiveSoToExtractSi_RoundTripsBytesExactly()
    {
        string destDir = CliFixtureFiles.CreateScratchDir();
        string logPath = Path.Combine(destDir, "log.txt");
        string exe = CliProcessRunner.ExePath;

        // Exercises the actual documented recipe (CliHelpText.Text / CLI.md) for a real shell
        // pipeline, not just .NET's own RedirectStandardInput/Output plumbing — this is the test
        // that proves -si/-so actually work "in a pipeline", per T-F116's empirical finding that
        // native PowerShell 5.1 corrupts binary data between two executables while cmd /c "..."
        // does not, on any shell version.
        string pipeline = $"\"{exe}\" a -so out.zip \"{CliFixtureFiles.SourceFileA}\" | " +
                           $"\"{exe}\" x -si -o\"{destDir}\" > \"{logPath}\" 2>&1";

        // Arguments (a raw string), not ArgumentList: .NET's ArgumentList re-escapes each element
        // independently, which mangles a pipeline string that already contains its own embedded
        // quotes/pipes/redirects — confirmed empirically (ArgumentList produced a garbled command
        // line cmd.exe couldn't parse, exit 255; the literal `cmd /c "..."` form below matches
        // exactly what a user would type and is what CliHelpText.Text documents).
        var startInfo = new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            Arguments = $"/c \"{pipeline}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using System.Diagnostics.Process process = System.Diagnostics.Process.Start(startInfo)!;
        bool exited = process.WaitForExit(30000);

        exited.Should().BeTrue();
        process.ExitCode.Should().Be(0);
        // Single-file archive → isSingleRootFile, no smart-folder wrapper.
        File.ReadAllText(Path.Combine(destDir, "a.txt")).Should().Be("hello world");
    }

    private static (int ExitCode, string StdOut, string StdErr) RunTarExe(params string[] args)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo(@"C:\Windows\System32\tar.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string arg in args)
            startInfo.ArgumentList.Add(arg);

        using System.Diagnostics.Process process = System.Diagnostics.Process.Start(startInfo)!;
        string stdOut = process.StandardOutput.ReadToEnd();
        string stdErr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdOut, stdErr);
    }
}
