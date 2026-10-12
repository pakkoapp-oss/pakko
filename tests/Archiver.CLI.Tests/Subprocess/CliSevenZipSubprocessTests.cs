using System.Text;
using FluentAssertions;

namespace Archiver.CLI.Tests.Subprocess;

/// <summary>
/// T-F342 (the CLI wave): 'pakko a -t7z' through the real exe and the real tar.exe. What 7-Zip
/// says of each method is checked where the vendored 7za.exe is (Archiver.Core.PerformanceTests
/// and the smoke rows); here the archive's own bytes say enough: the 7z signature, a bzip2 stream
/// where bzip2 was asked for, the plain text where nothing is compressed.
/// </summary>
public sealed class CliSevenZipSubprocessTests : IAsyncLifetime
{
    private static readonly byte[] Signature = [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C];
    private const int PackedDataOffset = 32;   // the 7z signature header is 32 bytes
    // tar.exe takes names through the system ANSI code page and Core refuses a name that page
    // cannot carry (T-F266): Cyrillic where it can, Latin-1 on an en-US machine such as CI's.
    private static readonly string NonAsciiName = new[] { "\u0437\u0432\u0456\u0442.txt", "caf\u00E9.txt" }.First(SurvivesTheAnsiCodePage);
    private static readonly string Text = string.Concat(Enumerable.Repeat("The quick brown fox jumps over the lazy dog. ", 400));

    private readonly TestTempFolder _temp = new("pakko-cli-7z-");

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _temp.DeleteAsync();

    private string Source(string name)
    {
        string folder = Path.Combine(_temp.Path, "src");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, name);
        File.WriteAllText(path, Text);
        return path;
    }

    private string Archive(string name = "out.7z") => Path.Combine(_temp.Path, name);

    private static bool SurvivesTheAnsiCodePage(string name)
    {
        // null: the ANSI code page is UTF-8, which carries any name.
        Encoding? ansi = CodePagesEncodingProvider.Instance.GetEncoding(0);
        return ansi is null || ansi.GetString(ansi.GetBytes(name)) == name;
    }

    private static bool Holds(byte[] haystack, byte[] needle) => haystack.AsSpan().IndexOf(needle) >= 0;

    [SevenZipWriterFact]
    public void Archive_SevenZip_WritesARealSevenZipThatListsAndExtractsBack()
    {
        string first = Source("a.txt");
        string second = Source(NonAsciiName);

        (int exitCode, _, string stdErr) = CliProcessRunner.Run("a", "-t7z", Archive(), first, second);

        exitCode.Should().Be(0, stdErr);
        byte[] bytes = File.ReadAllBytes(Archive());
        bytes.AsSpan(0, Signature.Length).ToArray().Should().Equal(Signature, "a plain 'tar -cf out.7z' would write ustar under this name");
        bytes.Length.Should().BeLessThan(Text.Length, "the default method compresses");

        (int listExit, string listed, _) = CliProcessRunner.Run("l", Archive());
        listExit.Should().Be(0);
        listed.Should().Contain("a.txt");

        string dest = Path.Combine(_temp.Path, "back");
        (int extractExit, _, string extractErr) = CliProcessRunner.Run("x", $"-o{dest}", Archive());
        extractExit.Should().Be(0, extractErr);
        File.ReadAllText(Path.Combine(dest, "a.txt")).Should().Be(Text);
        File.ReadAllText(Path.Combine(dest, NonAsciiName)).Should().Be(Text);
    }

    [SevenZipWriterFact]
    public void Archive_SevenZipWithBzip2_PacksABzip2Stream()
    {
        (int exitCode, _, string stdErr) = CliProcessRunner.Run("a", "-t7z", "-m0=bzip2", Archive(), Source("a.txt"));

        exitCode.Should().Be(0, stdErr);
        byte[] bytes = File.ReadAllBytes(Archive());
        Encoding.ASCII.GetString(bytes, PackedDataOffset, 3).Should().Be("BZh");
    }

    [SevenZipWriterFact]
    public void Archive_SevenZipAtLevelZero_StoresTheTextAsItIs()
    {
        (int exitCode, _, string stdErr) = CliProcessRunner.Run("a", "-t7z", "-mx=0", Archive(), Source("a.txt"));

        exitCode.Should().Be(0, stdErr);
        Holds(File.ReadAllBytes(Archive()), Encoding.ASCII.GetBytes(Text)).Should().BeTrue();
    }

    // Level 0 stores whatever -m0 names: Core turns "no compression" into the copy method.
    [SevenZipWriterFact]
    public void Archive_SevenZipWithAMethodAtLevelZero_StillStores()
    {
        (int exitCode, _, string stdErr) = CliProcessRunner.Run("a", "-t7z", "-m0=bzip2", "-mx=0", Archive(), Source("a.txt"));

        exitCode.Should().Be(0, stdErr);
        byte[] bytes = File.ReadAllBytes(Archive());
        Holds(bytes, Encoding.ASCII.GetBytes(Text)).Should().BeTrue();
        Encoding.ASCII.GetString(bytes, PackedDataOffset, 3).Should().NotBe("BZh");
    }

    [SevenZipWriterFact]
    public void Archive_SevenZipToStdout_StreamsTheSameKindOfArchive()
    {
        (int exitCode, byte[] stdOut, string stdErr) = CliProcessRunner.RunWithBinaryStdio(
            stdinBytes: [], "a", "-t7z", "-so", "out.7z", Source("a.txt"));

        exitCode.Should().Be(0, stdErr);
        stdOut.AsSpan(0, Signature.Length).ToArray().Should().Equal(Signature);
    }

    [SevenZipWriterFact]
    public void Archive_SevenZipWithRecoveryData_IsReportedIntactByTest()
    {
        (int exitCode, _, string stdErr) = CliProcessRunner.Run("a", "-t7z", "-rr5", Archive(), Source("a.txt"));

        exitCode.Should().Be(0, stdErr);
        File.Exists(Archive() + ".par2").Should().BeTrue();
        (int testExit, string testOut, string testErr) = CliProcessRunner.Run("t", Archive());
        testExit.Should().Be(0, testErr);
        (testOut + testErr).Should().Contain("recovery data intact");
    }

    [SevenZipWriterFact]
    public void Archive_SevenZipOverAnExistingArchive_KeepsItWithoutYesAndReplacesItWithYes()
    {
        string source = Source("a.txt");
        File.WriteAllText(Archive(), "not an archive");

        (int skipped, _, _) = CliProcessRunner.Run("a", "-t7z", Archive(), source);
        skipped.Should().NotBe(0);
        File.ReadAllText(Archive()).Should().Be("not an archive");

        (int replaced, _, string stdErr) = CliProcessRunner.Run("a", "-t7z", "-y", Archive(), source);
        replaced.Should().Be(0, stdErr);
        File.ReadAllBytes(Archive()).AsSpan(0, Signature.Length).ToArray().Should().Equal(Signature);
    }

    [SevenZipWriterFact]
    public void Archive_SevenZipOfAMissingSource_FailsAndLeavesNothing()
    {
        (int exitCode, _, string stdErr) = CliProcessRunner.Run("a", "-t7z", Archive(), Path.Combine(_temp.Path, "nothing-here.txt"));

        exitCode.Should().Be(2);
        stdErr.Should().Contain("nothing-here.txt");
        Directory.GetFileSystemEntries(_temp.Path).Should().BeEmpty("no archive, no temporary file and no writer folder");
    }

    // The refusals need no writer: they are made before anything runs.
    [Theory]
    [InlineData("-t7z", "-pSecret-1", "out.7z")]
    [InlineData("-t7z", "-m0=ppmd", "out.7z")]
    [InlineData("-t7z", "-m0=lzma2:d=64m", "out.7z")]
    [InlineData("-tzip", "-m0=lzma2", "out.zip")]
    [InlineData("-tzip", "-mx=5", "out.7z")]
    [InlineData("-t7z", "-mx=5", "out.zip")]
    public void Archive_SevenZipSwitchesThatContradict_ExitSevenAndWriteNothing(string first, string second, string name)
    {
        string source = Source("a.txt");

        (int exitCode, _, string stdErr) = CliProcessRunner.Run("a", first, second, Archive(name), source);

        exitCode.Should().Be(7, stdErr);
        File.Exists(Archive(name)).Should().BeFalse();
    }

    [Fact]
    public void Archive_ANameEndingInSevenZipWithNoType_ExitsSevenWithTheSwitchToAdd()
    {
        (int exitCode, _, string stdErr) = CliProcessRunner.Run("a", Archive("backup.7z"), Source("a.txt"));

        exitCode.Should().Be(7);
        stdErr.Should().Contain("-t7z");
        File.Exists(Archive("backup.7z")).Should().BeFalse();
    }

    [SevenZipWriterFact]
    public void Info_OnASystemWithTheWriter_SaysSevenZipIsCreated()
    {
        (int exitCode, string stdOut, _) = CliProcessRunner.Run("i");

        exitCode.Should().Be(0);
        stdOut.Split('\n').Single(line => line.TrimStart().StartsWith("7z ", StringComparison.Ordinal))
            .Should().Contain("create, extract, list");
    }
}
