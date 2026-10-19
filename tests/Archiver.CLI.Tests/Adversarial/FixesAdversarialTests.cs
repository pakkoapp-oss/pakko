using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Archiver.CLI.Tests.Subprocess;
using FluentAssertions;

namespace Archiver.CLI.Tests.Adversarial;

/// <summary>
/// T-F419 red team, pass 3: attacks on the fixes of the first two passes (links inside -o, -rr's
/// names, Windows-folded twins, device names, -so to a reader that stops, -sdel's real-path
/// checks, \\?\ archives, -o as Windows reads it, what is in the way at commit, wrong-command
/// switches). The rule each checks: no byte the user did not ask to lose goes, and exit 0 only
/// when everything asked for was done.
/// </summary>
public sealed class FixesAdversarialTests : IDisposable
{
    private const string TarExe = @"C:\Windows\System32\tar.exe";
    private readonly AdversarialScratch _scratch = new();

    public void Dispose() => _scratch.Dispose();

    private string Read(string relative) => File.ReadAllText(@"\\?\" + _scratch.Inside(relative));

    /// <summary>Every file's content under a folder, read through \\?\ so Win32-trimmed names show.</summary>
    private string[] Contents(string relative) =>
        [.. _scratch.FilesUnder(relative).Select(name => File.ReadAllText(@"\\?\" + Path.Combine(_scratch.Inside(relative), name))).Order(StringComparer.Ordinal)];

    /// <summary>Every name (files and folders) anywhere under the root, through \\?\.</summary>
    private string[] AllNames() =>
        [.. Directory.EnumerateFileSystemEntries(@"\\?\" + _scratch.Root, "*", SearchOption.AllDirectories).Select(p => Path.GetFileName(p)!)];

    private string TarGz(string relative, params (string Name, string Content)[] entries)
    {
        string path = _scratch.Inside(relative);
        using (FileStream file = File.Create(path))
        using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
        using (var writer = new TarWriter(gzip, TarEntryFormat.Pax))
        {
            foreach ((string name, string content) in entries)
            {
                var entry = new PaxTarEntry(TarEntryType.RegularFile, name)
                {
                    DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content)),
                };
                writer.WriteEntry(entry);
            }
        }
        return path;
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

    // =====================================================================================
    // 1. Links already inside the destination
    // =====================================================================================

    // A file symlink at the entry's own name: replacing it must replace the link, never write the
    // file it points to (7za replaces the link too).
    [RequiresSymlinkTheory]
    [InlineData("-aoa")]
    [InlineData("-y")]
    [InlineData("-aot")]
    public void Zip_FileSymlinkAtTheEntryName_NeverWritesTheFileItPointsTo(string overwrite)
    {
        _scratch.Zip("arc.zip", ("x.txt", "NEW"));
        string outside = _scratch.WriteFile(@"outside\x.txt", "PRECIOUS");
        _scratch.Folder("out");
        File.CreateSymbolicLink(_scratch.Inside(@"out\x.txt"), outside);

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", "arc.zip", "-oout", overwrite);

        File.ReadAllText(outside).Should().Be("PRECIOUS", $"x {overwrite} must not write through out\\x.txt (exit {exitCode}, stderr: {stdErr})");
        exitCode.Should().Be(0, stdErr);
        File.ReadAllText(_scratch.Inside(@"out\x.txt")).Should().Be("NEW");
    }

    [RequiresSymlinkTheory]
    [InlineData("-aoa")]
    [InlineData("-aou")]
    [InlineData("-aot")]
    public void Zip_DirectorySymlinkInDestination_NothingIsWrittenBehindIt(string overwrite)
    {
        _scratch.Zip("arc.zip", ("link/x.txt", "NEW"), ("link/new.txt", "PLANTED"));
        string outside = _scratch.WriteFile(@"outside\x.txt", "PRECIOUS");
        _scratch.Folder("out");
        Directory.CreateSymbolicLink(_scratch.Inside(@"out\link"), _scratch.Inside("outside"));

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", "arc.zip", "-oout", overwrite);

        Directory.GetFiles(_scratch.Inside("outside")).Select(Path.GetFileName).Should().Equal(["x.txt"], $"exit {exitCode}, stderr: {stdErr}");
        File.ReadAllText(outside).Should().Be("PRECIOUS");
        exitCode.Should().NotBe(0, "two entries were left out");
    }

    // The link is an intermediate folder two levels down, under a real folder.
    [Fact]
    public void Zip_JunctionDeepInsideDestination_NothingIsWrittenBehindIt()
    {
        _scratch.Zip("arc.zip", ("a/b/c/x.txt", "NEW"), ("a/keep.txt", "KEEP"));
        string outside = _scratch.WriteFile(@"outside\c\x.txt", "PRECIOUS");
        _scratch.Folder(@"out\a");
        _scratch.Junction(@"out\a\b", "outside");

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", "arc.zip", "-oout", "-aoa");

        File.ReadAllText(outside).Should().Be("PRECIOUS", $"exit {exitCode}, stderr: {stdErr}");
        Read(@"out\a\keep.txt").Should().Be("KEEP");
        exitCode.Should().NotBe(0, "an entry was left out");
    }

    // A left-out entry is not "everything extracted".
    [Fact]
    public void Zip_JunctionInDestination_LeftOutEntry_IsNotExitZero()
    {
        _scratch.Zip("arc.zip", ("link/x.txt", "NEW"));
        _scratch.Folder("out");
        _scratch.Folder("outside");
        _scratch.Junction(@"out\link", "outside");

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", "arc.zip", "-oout");

        _scratch.FilesUnder("outside").Should().BeEmpty();
        exitCode.Should().NotBe(0, $"the only entry was not written; stderr: {stdErr}");
        stdErr.Should().Contain("x.txt");
    }

    // A junction whose target is inside -o itself: still a link, still left out; the real folder keeps its file.
    [Fact]
    public void Zip_JunctionPointingInsideDestination_TargetFileIsKept()
    {
        _scratch.Zip("arc.zip", ("link/x.txt", "NEW"), ("real/y.txt", "Y"));
        _scratch.WriteFile(@"out\real\x.txt", "OLD");
        _scratch.Junction(@"out\link", @"out\real");

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", "arc.zip", "-oout", "-aoa");

        Read(@"out\real\x.txt").Should().Be("OLD", $"exit {exitCode}, stderr: {stdErr}");
        Read(@"out\real\y.txt").Should().Be("Y");
        exitCode.Should().NotBe(0);
    }

    // A junction where the FILE goes: -aoa reports a folder in the way, -aot renames the junction
    // itself; the folder behind it keeps its content and its place.
    [Theory]
    [InlineData("-aoa", 2)]
    [InlineData("-aot", 0)]
    public void Zip_JunctionAtTheEntryFileName_TargetFolderIsUntouched(string overwrite, int expectedExit)
    {
        _scratch.Zip("arc.zip", ("x.txt", "NEW"));
        string inner = _scratch.WriteFile(@"outside\inner.txt", "PRECIOUS");
        _scratch.Folder("out");
        _scratch.Junction(@"out\x.txt", "outside");

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", "arc.zip", "-oout", overwrite);

        File.ReadAllText(inner).Should().Be("PRECIOUS", $"exit {exitCode}, stderr: {stdErr}");
        Directory.GetFileSystemEntries(_scratch.Inside("outside")).Should().HaveCount(1);
        exitCode.Should().Be(expectedExit, stdErr);
        if (overwrite == "-aot")
            File.ReadAllText(_scratch.Inside(@"out\x.txt")).Should().Be("NEW");
    }

    [Fact]
    public void Zip_JunctionInDestination_WithSpe_NothingIsWrittenBehindIt()
    {
        _scratch.Zip("arc.zip", ("out/link/x.txt", "NEW"));
        string outside = _scratch.WriteFile(@"outside\x.txt", "PRECIOUS");
        _scratch.Folder("out");
        _scratch.Junction(@"out\link", "outside");

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", "arc.zip", "-oout", "-spe", "-aoa");

        File.ReadAllText(outside).Should().Be("PRECIOUS", $"exit {exitCode}, stderr: {stdErr}");
        exitCode.Should().NotBe(0);
    }

    [RequiresSymlinkFact]
    public void TarGz_FileSymlinkAtTheEntryName_Aoa_NeverWritesTheFileItPointsTo()
    {
        TarGz("arc.tar.gz", ("x.txt", "NEW"));
        string outside = _scratch.WriteFile(@"outside\x.txt", "PRECIOUS");
        _scratch.Folder("out");
        File.CreateSymbolicLink(_scratch.Inside(@"out\x.txt"), outside);

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", "arc.tar.gz", "-oout", "-aoa");

        File.ReadAllText(outside).Should().Be("PRECIOUS", $"exit {exitCode}, stderr: {stdErr}");
        exitCode.Should().Be(0, stdErr);
        File.ReadAllText(_scratch.Inside(@"out\x.txt")).Should().Be("NEW");
    }

    [Theory]
    [InlineData("-aoa")]
    [InlineData("-aot")]
    public void TarGz_JunctionInDestination_NewAndExistingFilesBehindItUntouched(string overwrite)
    {
        TarGz("arc.tar.gz", ("link/x.txt", "NEW"), ("link/planted.txt", "PLANTED"));
        string outside = _scratch.WriteFile(@"outside\x.txt", "PRECIOUS");
        _scratch.Folder("out");
        _scratch.Junction(@"out\link", "outside");

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", "arc.tar.gz", "-oout", overwrite);

        Directory.GetFiles(_scratch.Inside("outside")).Select(Path.GetFileName).Should().Equal(["x.txt"], $"exit {exitCode}, stderr: {stdErr}");
        File.ReadAllText(outside).Should().Be("PRECIOUS");
        exitCode.Should().NotBe(0);
    }

    [Fact]
    public void SevenZ_JunctionInDestination_Aoa_NothingIsWrittenBehindIt()
    {
        _scratch.WriteFile(@"stage\link\x.txt", "NEW");
        RunTar(_scratch.Inside("stage"), "--format=7zip", "-cf", _scratch.Inside("arc.7z"), "link");
        string outside = _scratch.WriteFile(@"outside\x.txt", "PRECIOUS");
        _scratch.Folder("out");
        _scratch.Junction(@"out\link", "outside");

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", "arc.7z", "-oout", "-aoa");

        File.ReadAllText(outside).Should().Be("PRECIOUS", $"exit {exitCode}, stderr: {stdErr}");
        exitCode.Should().NotBe(0);
    }

    // =====================================================================================
    // 2. -rr and files already under the set's names
    // =====================================================================================

    // Another archive's PAR2 volume under a name RemoveStaleVolumes looks at (o.zip.vol*.par2,
    // other padding than the new set's): it is not this archive's set and must stay.
    [Fact]
    public void Rr_AnotherArchivesPar2VolumeUnderAStaleVolumeName_IsKept()
    {
        _scratch.WriteFile("b.txt", "B");
        _scratch.WriteFile("other.txt", "OTHER");
        (int otherExit, _, string otherErr) = _scratch.Pakko("a", "-rr", "other.zip", "other.txt");
        otherExit.Should().Be(0, otherErr);
        string otherVolume = Directory.GetFiles(_scratch.Root, "other.zip.vol*.par2").Single();
        string planted = _scratch.Inside("o.zip.vol00+99.par2");
        File.Move(otherVolume, planted);
        byte[] before = File.ReadAllBytes(planted);

        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "-rr", "o.zip", "b.txt");

        File.Exists(planted).Should().BeTrue($"o.zip.vol00+99.par2 holds other.zip's recovery data, not o.zip's (exit {exitCode}, stderr: {stdErr})");
        File.ReadAllBytes(planted).Should().Equal(before);
    }

    // Same, the planted file is another archive's index under o.zip.par2: kept, not exit 0.
    [Fact]
    public void Rr_AnotherArchivesPar2IndexUnderTheIndexName_IsKeptAndNotExitZero()
    {
        _scratch.WriteFile("b.txt", "B");
        _scratch.WriteFile("other.txt", "OTHER");
        (int otherExit, _, string otherErr) = _scratch.Pakko("a", "-rr", "other.zip", "other.txt");
        otherExit.Should().Be(0, otherErr);
        File.Move(_scratch.Inside("other.zip.par2"), _scratch.Inside("o.zip.par2"));
        byte[] before = File.ReadAllBytes(_scratch.Inside("o.zip.par2"));

        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "-rr", "o.zip", "b.txt");

        File.ReadAllBytes(_scratch.Inside("o.zip.par2")).Should().Equal(before, $"exit {exitCode}, stderr: {stdErr}");
        exitCode.Should().Be(2, "CLI.md: a failed set is exit 2 with the archive kept");
        File.Exists(_scratch.Inside("o.zip")).Should().BeTrue();
    }

    // The other archive's index under the base-name spelling (o.par2), which the set locator
    // also reads for o.zip: a set naming other.zip is not o.zip's to remove.
    [Fact]
    public void Rr_AnotherArchivesPar2UnderTheBaseName_IsKept()
    {
        _scratch.WriteFile("b.txt", "B");
        _scratch.WriteFile("other.txt", "OTHER");
        (int otherExit, _, string otherErr) = _scratch.Pakko("a", "-rr", "other.zip", "other.txt");
        otherExit.Should().Be(0, otherErr);
        File.Move(_scratch.Inside("other.zip.par2"), _scratch.Inside("o.par2"));
        string volume = Directory.GetFiles(_scratch.Root, "other.zip.vol*.par2").Single();
        File.Move(volume, _scratch.Inside("o.vol0+1.par2"));

        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "-rr", "o.zip", "b.txt");

        File.Exists(_scratch.Inside("o.par2")).Should().BeTrue($"exit {exitCode}, stderr: {stdErr}");
        File.Exists(_scratch.Inside("o.vol0+1.par2")).Should().BeTrue();
        exitCode.Should().Be(0, stdErr);
    }

    // The archive's own set, damaged: not wholly PAR2 data of this archive, so kept and exit 2;
    // never exit 0 with the stale, damaged set left as if it were this archive's.
    [Fact]
    public void Rr_DamagedOwnSet_OverRewrittenArchive_IsNotExitZeroWithTheOldSet()
    {
        _scratch.WriteFile("b.txt", "B");
        (int firstExit, _, string firstErr) = _scratch.Pakko("a", "-rr", "o.zip", "b.txt");
        firstExit.Should().Be(0, firstErr);
        string index = _scratch.Inside("o.zip.par2");
        byte[] damaged = File.ReadAllBytes(index);
        damaged[^5] ^= 0xFF;
        File.WriteAllBytes(index, damaged);
        _scratch.WriteFile("b.txt", "B-CHANGED-LONGER");

        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "-y", "-rr", "o.zip", "b.txt");

        if (exitCode == 0)
        {
            (int testExit, _, string testErr) = _scratch.Pakko("t", "o.zip");
            testExit.Should().Be(0, $"exit 0 on 'a -rr' means the new set protects the new archive; t says: {testErr}");
        }
        else
        {
            exitCode.Should().Be(2, stdErr);
        }
    }

    [Fact]
    public void Rr_FolderNamedLikeTheIndex_IsExitTwoArchiveKeptFolderKept()
    {
        _scratch.WriteFile("b.txt", "B");
        _scratch.WriteFile(@"o.zip.par2\mine.txt", "MINE");

        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "-rr", "o.zip", "b.txt");

        Read(@"o.zip.par2\mine.txt").Should().Be("MINE");
        exitCode.Should().Be(2, stdErr);
        File.Exists(_scratch.Inside("o.zip")).Should().BeTrue();
    }

    [Fact]
    public void Rr_ReadOnlyForeignIndex_IsKeptAndExitTwo()
    {
        _scratch.WriteFile("b.txt", "B");
        string index = _scratch.WriteFile("o.zip.par2", "my notes");
        File.SetAttributes(index, FileAttributes.ReadOnly);
        try
        {
            (int exitCode, _, string stdErr) = _scratch.Pakko("a", "-y", "-rr", "o.zip", "b.txt");

            File.ReadAllText(index).Should().Be("my notes");
            exitCode.Should().Be(2, stdErr);
        }
        finally
        {
            File.SetAttributes(index, FileAttributes.Normal);
        }
    }

    // A failed set leaves sources Partial: -sdel must delete nothing.
    [Fact]
    public void Rr_WithSdel_ForeignIndex_DeletesNoSource()
    {
        _scratch.WriteFile(@"src\b.txt", "B");
        _scratch.WriteFile("o.zip.par2", "my notes");

        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "-rr", "-sdel", "o.zip", "src");

        Read(@"src\b.txt").Should().Be("B", $"the set was not written, so the archive is unprotected (exit {exitCode}, stderr: {stdErr})");
        Read("o.zip.par2").Should().Be("my notes");
        exitCode.Should().Be(2);
    }

    // =====================================================================================
    // 3. ZIP entries Windows folds to one name
    // =====================================================================================

    // The fix compares the file names only; "d./x.txt" and "d/x.txt" differ in a FOLDER segment
    // and land on one file all the same. 7za 26.02 measured: "x twin.zip -y" writes d\x.txt and
    // d_\x.txt, exit 0.
    [Theory]
    [InlineData("-y")]
    [InlineData("-aoa")]
    [InlineData("-aot")]
    [InlineData("-aos")]
    [InlineData(null)]
    public void Zip_FolderSegmentTrailingDotTwin_KeepsBothOrIsNotExitZero(string? overwrite)
    {
        _scratch.Zip("arc.zip", ("d/x.txt", "PLAIN"), ("d./x.txt", "DOT"));
        string[] args = overwrite is null ? ["x", "arc.zip", "-oout"] : ["x", "arc.zip", "-oout", overwrite];

        (int exitCode, _, string stdErr) = _scratch.Pakko(args);

        string[] contents = Contents("out");
        contents.Should().Contain("PLAIN", $"the first entry must survive the second (exit {exitCode}, files: {string.Join(", ", _scratch.FilesUnder("out"))}, stderr: {stdErr})");
        if (exitCode == 0)
            contents.Should().Contain("DOT", $"exit 0 means both entries were extracted; stderr: {stdErr}");
    }

    [Theory]
    [InlineData("-y")]
    [InlineData("-aoa")]
    public void Zip_FolderSegmentTrailingSpaceTwin_KeepsBothOrIsNotExitZero(string overwrite)
    {
        _scratch.Zip("arc.zip", ("d/x.txt", "PLAIN"), ("d /x.txt", "SPACE"));

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", "arc.zip", "-oout", overwrite);

        string[] contents = Contents("out");
        if (exitCode == 0)
            contents.Should().Contain("PLAIN").And.Contain("SPACE", $"files: {string.Join(", ", _scratch.FilesUnder("out"))}; stderr: {stdErr}");
        else
            contents.Should().Contain("PLAIN");
    }

    [Theory]
    [InlineData("-y")]
    [InlineData("-aoa")]
    [InlineData("-aot")]
    public void Zip_ThreeWayTrimTwins_AllThreeKept(string overwrite)
    {
        _scratch.Zip("arc.zip", ("a.txt", "PLAIN"), ("a.txt.", "DOT"), ("a.txt ", "SPACE"));

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", "arc.zip", "-oout", overwrite);

        exitCode.Should().Be(0, stdErr);
        Contents("out").Should().Equal("DOT", "PLAIN", "SPACE");
    }

    [Fact]
    public void Zip_CaseAndDotTwin_Y_BothKept()
    {
        _scratch.Zip("arc.zip", ("a.txt", "PLAIN"), ("A.TXT.", "UPPERDOT"));

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", "arc.zip", "-oout", "-y");

        exitCode.Should().Be(0, stdErr);
        Contents("out").Should().Equal("PLAIN", "UPPERDOT");
    }

    // Under -aos the twin is skipped as any other in-run clash would be: exit 1, the first kept.
    [Fact]
    public void Zip_TrimTwin_Aos_FirstKeptAndExitOne()
    {
        _scratch.Zip("arc.zip", ("a.txt", "PLAIN"), ("a.txt.", "DOT"));

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", "arc.zip", "-oout", "-aos");

        Contents("out").Should().Contain("PLAIN");
        if (exitCode == 0)
            Contents("out").Should().Contain("DOT", stdErr);
        else
            exitCode.Should().Be(1, stdErr);
    }

    // A twin against a file already on disk: -y may overwrite the disk file (asked), but the two
    // entries must still both survive.
    [Fact]
    public void Zip_TrimTwin_OverAnExistingFile_Y_BothEntriesSurvive()
    {
        _scratch.Zip("arc.zip", ("a.txt", "PLAIN"), ("a.txt.", "DOT"));
        _scratch.WriteFile(@"out\a.txt", "OLD");

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", "arc.zip", "-oout", "-y");

        exitCode.Should().Be(0, stdErr);
        Contents("out").Should().Contain("PLAIN").And.Contain("DOT");
    }

    // The tar route was not part of the fix: tar.exe writes "a.txt." onto "a.txt".
    [Theory]
    [InlineData("-y")]
    [InlineData("-aoa")]
    public void TarGz_TrimTwin_KeepsBothOrIsNotExitZero(string overwrite)
    {
        TarGz("arc.tar.gz", ("a.txt", "PLAIN"), ("a.txt.", "DOT"));

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", "arc.tar.gz", "-oout", overwrite);

        string[] contents = Contents("out");
        if (exitCode == 0)
            contents.Should().Contain("PLAIN").And.Contain("DOT", $"files: {string.Join(", ", _scratch.FilesUnder("out"))}; stderr: {stdErr}");
        else
            exitCode.Should().BeOneOf(1, 2);
    }

    [Theory]
    [InlineData("-y")]
    [InlineData("-aou")]
    public void TarGz_FolderSegmentTrailingDotTwin_KeepsBothOrIsNotExitZero(string overwrite)
    {
        TarGz("arc.tar.gz", ("d/x.txt", "PLAIN"), ("d./x.txt", "DOT"));

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", "arc.tar.gz", "-oout", overwrite);

        string[] contents = Contents("out");
        contents.Should().Contain("PLAIN", $"the first entry must survive the second (exit {exitCode}, files: {string.Join(", ", _scratch.FilesUnder("out"))}, stderr: {stdErr})");
        if (exitCode == 0)
            contents.Should().Contain("DOT", stdErr);
    }

    // T-F419 triage: the trimmed name first, and a file-name twin, through tar.exe too.
    [Theory]
    [InlineData("d./x.txt", "d/x.txt")]
    [InlineData("a.txt", "a.txt.")]
    [InlineData("a.txt.", "a.txt")]
    [InlineData("a.txt", "a.txt ")]
    public void TarGz_TwinsWindowsTrimsToOne_Y_BothKept(string first, string second)
    {
        TarGz("arc.tar.gz", (first, "FIRST"), (second, "SECOND"));

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", "arc.tar.gz", "-oout", "-y");

        exitCode.Should().Be(0, stdErr);
        Contents("out").Should().BeEquivalentTo(["FIRST", "SECOND"], $"files: {string.Join(", ", _scratch.FilesUnder("out"))}");
    }

    [Fact]
    public void Zip_FolderSegmentTrailingDotTwin_Aou_BothKept()
    {
        _scratch.Zip("arc.zip", ("d/x.txt", "PLAIN"), ("d./x.txt", "DOT"));

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", "arc.zip", "-oout", "-aou");

        exitCode.Should().Be(0, stdErr);
        Contents("out").Should().Equal("DOT", "PLAIN");
    }

    // An entry named as the 8.3 alias Windows gives an earlier entry inside the staging folder.
    [RequiresShortNamesTheory]
    [InlineData("-y")]
    [InlineData("-aou")]
    [InlineData(null)]
    public void Zip_EntryNamedAsAnotherEntrysShortName_BothKeptOrNotExitZero(string? overwrite)
    {
        _scratch.Zip("arc.zip", ("longfilename.txt", "LONG"), ("LONGFI~1.TXT", "SHORT"));
        string[] args = overwrite is null ? ["x", "arc.zip", "-oout"] : ["x", "arc.zip", "-oout", overwrite];

        (int exitCode, _, string stdErr) = _scratch.Pakko(args);

        string[] contents = Contents("out");
        contents.Should().Contain("LONG", $"the first entry must never be overwritten by the second's alias; files: {string.Join(", ", _scratch.FilesUnder("out"))}; exit {exitCode}; stderr: {stdErr}");
        if (exitCode == 0)
            contents.Should().Contain("SHORT");
    }

    // =====================================================================================
    // 4. Device names
    // =====================================================================================

    [Theory]
    [InlineData("CONIN$")]
    [InlineData("CONOUT$")]
    [InlineData("COM1.")]
    [InlineData("aux ")]
    [InlineData("lpt9 . ")]
    [InlineData(@"\\.\NUL")]
    public void Sae_DeviceArchiveName_IsRefusedAndWritesNothing(string device)
    {
        _scratch.WriteFile("b.txt", "B");

        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "-sae", device, "b.txt");

        stdErr.Should().NotContain("Exception").And.NotContain("Unexpected error");
        exitCode.Should().Be(2, stdErr);
        AllNames().Should().Equal(["b.txt"], "refused before anything is written");
    }

    [Fact]
    public void Sae_SuperscriptComDevice_IsRefusedAndWritesNothing()
    {
        _scratch.WriteFile("b.txt", "B");
        string device = "COM" + (char)0xB9;

        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "-sae", device, "b.txt");

        exitCode.Should().Be(2, stdErr);
        AllNames().Should().Equal(["b.txt"]);
    }

    // A device in the folder part: either a real archive that can be opened, or an error with no
    // device-named folder left behind; never exit 0 for an archive that is not there.
    // 7za 26.02 measured: "a y\NUL\o.zip" is "System ERROR", exit 2; "a x\CON\o.zip" makes a
    // folder CON and the archive in it, exit 0 - see DeviceFolderInTheArchivePath_MatchesSevenZip.
    [Theory]
    [InlineData(@"x\NUL\o.zip")]
    [InlineData(@"x\NUL.\o.zip")]
    [InlineData(@"x\nul \o.zip")]
    public void DeviceInTheFolderPart_IsNotASilentSuccess(string archive)
    {
        _scratch.WriteFile("b.txt", "B");

        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "-sae", archive, "b.txt");

        stdErr.Should().NotContain("Exception").And.NotContain("Unexpected error");
        string literal = @"\\?\" + _scratch.Inside(archive);
        if (exitCode == 0)
        {
            File.Exists(literal).Should().BeTrue($"exit 0 claims {archive} was written; stderr: {stdErr}");
        }
        else
        {
            exitCode.Should().Be(2, stdErr);
        }
        AllNames().Should().NotContain(name => name.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || name.Equals("CON", StringComparison.OrdinalIgnoreCase) || name.Equals("COM1", StringComparison.OrdinalIgnoreCase),
            $"a folder named as a device is one Explorer cannot remove (exit {exitCode}, stderr: {stdErr})");
    }

    [Theory]
    [InlineData(@"x\CON\o.zip")]
    [InlineData(@"x\COM1\o.zip")]
    public void DeviceFolderInTheArchivePath_MatchesSevenZip(string archive)
    {
        _scratch.WriteFile("b.txt", "B");

        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "-sae", archive, "b.txt");

        exitCode.Should().Be(0, stdErr);
        using ZipArchive zip = ZipFile.OpenRead(@"\\?\" + _scratch.Inside(archive));
        zip.Entries.Select(e => e.FullName).Should().Equal("b.txt");
    }

    // Not a device on Windows 11: CON.zip is an ordinary file name (DestinationFolder's comment).
    [Fact]
    public void ConDotZip_WithoutSae_IsAnOrdinaryArchive()
    {
        _scratch.WriteFile("b.txt", "B");

        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "CON.zip", "b.txt");

        exitCode.Should().Be(0, stdErr);
        using ZipArchive zip = ZipFile.OpenRead(@"\\?\" + _scratch.Inside("CON.zip"));
        zip.Entries.Select(e => e.FullName).Should().Equal("b.txt");
    }

    [Theory]
    [InlineData("-wNUL")]
    [InlineData("-wCON")]
    [InlineData(@"-w\\.\NUL")]
    public void WorkFolderIsADevice_IsExitTwoAndWritesNothing(string work)
    {
        _scratch.WriteFile("b.txt", "B");

        (int exitCode, _, string stdErr) = _scratch.Pakko("a", work, "o.zip", "b.txt");

        stdErr.Should().NotContain("Exception").And.NotContain("Unexpected error");
        exitCode.Should().Be(2, $"CLI.md: -w naming a folder that is not there is exit 2 before anything is written; stderr: {stdErr}");
        AllNames().Should().Equal(["b.txt"]);
    }

    [Theory]
    [InlineData("-oNUL")]
    [InlineData("-oCON")]
    [InlineData(@"-oout\NUL")]
    [InlineData(@"-o\\.\NUL")]
    [InlineData("-oCONIN$")]
    public void ExtractIntoADevice_IsNotExitZero(string output)
    {
        _scratch.Zip("arc.zip", ("a.txt", "A"));

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", "arc.zip", output);

        stdErr.Should().NotContain("Exception").And.NotContain("Unexpected error");
        exitCode.Should().NotBe(0, $"{output} names a device; a.txt was written nowhere a user can find it. stderr: {stdErr}");
        AllNames().Should().NotContain(name => name.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || name.Equals("CON", StringComparison.OrdinalIgnoreCase) || name.Equals("CONIN$", StringComparison.OrdinalIgnoreCase));
    }

    // =====================================================================================
    // 5. -so to a reader that stops
    // =====================================================================================

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(70000)]
    public void ExtractToStdout_ReaderStopsEarly_IsExitTwo(int bytes)
    {
        string big = _scratch.Inside("big.bin");
        File.WriteAllBytes(big, RandomNumberGenerator.GetBytes(8 * 1024 * 1024));
        (int packExit, _, string packErr) = _scratch.Pakko("a", "-mx0", "arc.zip", "big.bin");
        packExit.Should().Be(0, packErr);

        (int exitCode, int read, string stdErr) = StdoutReadingOnly(bytes, "x", "arc.zip", "-so");

        read.Should().Be(bytes, "harness");
        exitCode.Should().Be(2, $"the reader took {bytes} of 8 MiB; stderr: {stdErr}");
    }

    [Fact]
    public void ArchiveToStdout_ReaderClosesBeforeReading_SdelDeletesNothing()
    {
        _scratch.WriteFile(@"src\a.txt", "A");
        File.WriteAllBytes(_scratch.Inside(@"src\big.bin"), RandomNumberGenerator.GetBytes(8 * 1024 * 1024));

        (int exitCode, _, string stdErr) = StdoutReadingOnly(0, "a", "-sdel", "-mx0", "-so", "o.zip", "src");

        Read(@"src\a.txt").Should().Be("A", $"exit {exitCode}, stderr: {stdErr}");
        File.Exists(_scratch.Inside(@"src\big.bin")).Should().BeTrue();
        exitCode.Should().Be(2, stdErr);
    }

    // stdout redirected to NUL is a reader that takes everything: exit 0 (7za writes there too).
    [Theory]
    [InlineData("x arc.zip -so")]
    [InlineData("a -so o.zip a.txt")]
    public void ToStdout_RedirectedToNul_IsExitZero(string command)
    {
        _scratch.WriteFile("a.txt", "A");
        _scratch.Zip("arc.zip", ("a.txt", "A"));

        (int exitCode, string stdErr) = RunThroughCmd($"\"{CliProcessRunner.ExePath}\" {command} >NUL");

        exitCode.Should().Be(0, stdErr);
    }

    [Fact]
    public void ExtractToStdout_RedirectedToAFile_IsTheEntryBytes()
    {
        _scratch.Zip("arc.zip", ("a.txt", "ENTRY-BYTES"));

        (int exitCode, string stdErr) = RunThroughCmd($"\"{CliProcessRunner.ExePath}\" x arc.zip -so -bsp1 >out.bin");

        exitCode.Should().Be(0, stdErr);
        File.ReadAllText(_scratch.Inside("out.bin")).Should().Be("ENTRY-BYTES");
    }

    // =====================================================================================
    // 6. -sdel's real-path checks
    // =====================================================================================

    [RequiresAdminShareTheory]
    [InlineData(@"\\localhost\")]
    [InlineData(@"\\?\UNC\localhost\")]
    public void Sdel_FolderHoldingTheCurrentFolder_ThroughTheAdminShare_IsRefused(string prefix)
    {
        _scratch.WriteFile(@"src\work\w.txt", "W");
        _scratch.WriteFile(@"src\a.txt", "A");
        string src = _scratch.Inside("src");
        string unc = prefix + src[0] + "$" + src[2..];

        (int exitCode, _, string stdErr) = _scratch.PakkoIn(@"src\work", "a", "-sdel", @"..\..\o.zip", unc);

        File.Exists(_scratch.Inside(@"src\a.txt")).Should().BeTrue($"{unc} is the folder holding the current one; CLI.md refuses it with exit 7 before anything is written (exit {exitCode}, o.zip written: {File.Exists(_scratch.Inside("o.zip"))}, stderr: {stdErr})");
        Read(@"src\work\w.txt").Should().Be("W");
        exitCode.Should().Be(7, stdErr);
        File.Exists(_scratch.Inside("o.zip")).Should().BeFalse("refused before anything is written");
    }

    // The same folder through its volume GUID path: \\?\Volume{...}\Users\...\src.
    [Fact]
    public void Sdel_FolderHoldingTheCurrentFolder_ThroughTheVolumeGuidPath_IsRefused()
    {
        _scratch.WriteFile(@"src\work\w.txt", "W");
        _scratch.WriteFile(@"src\a.txt", "A");
        string src = _scratch.Inside("src");
        string volume = VolumeGuidPath.Of(Path.GetPathRoot(src)!);
        string spelled = volume + src[3..];
        Directory.Exists(spelled).Should().BeTrue("harness: the volume GUID path reaches the folder");

        (int exitCode, _, string stdErr) = _scratch.PakkoIn(@"src\work", "a", "-sdel", @"..\..\o.zip", spelled);

        File.Exists(_scratch.Inside(@"src\a.txt")).Should().BeTrue($"{spelled} is the folder holding the current one; CLI.md refuses it with exit 7 before anything is written (exit {exitCode}, o.zip written: {File.Exists(_scratch.Inside("o.zip"))}, stderr: {stdErr})");
        exitCode.Should().Be(7, stdErr);
        File.Exists(_scratch.Inside("o.zip")).Should().BeFalse();
    }

    // The current folder is a directory symlink into src; src named by its real path.
    [RequiresSymlinkFact]
    public void Sdel_CurrentFolderIsASymlinkIntoTheSource_IsRefused()
    {
        _scratch.WriteFile(@"src\work\w.txt", "W");
        _scratch.WriteFile(@"src\a.txt", "A");
        Directory.CreateSymbolicLink(_scratch.Inside("cwdlink"), _scratch.Inside(@"src\work"));

        (int exitCode, _, string stdErr) = _scratch.PakkoIn("cwdlink", "a", "-sdel", _scratch.Inside("o.zip"), _scratch.Inside("src"));

        Read(@"src\a.txt").Should().Be("A", $"exit {exitCode}, stderr: {stdErr}");
        exitCode.Should().Be(7, stdErr);
    }

    // 8.3 name of the source with the current folder entered through a junction.
    [RequiresShortNamesFact]
    public void Sdel_ShortNameSource_CurrentFolderThroughAJunction_IsRefused()
    {
        _scratch.WriteFile(@"longsourcefoldername\work\w.txt", "W");
        _scratch.WriteFile(@"longsourcefoldername\a.txt", "A");
        _scratch.Junction("jw", @"longsourcefoldername\work");
        string shortPath = ShortNames.Of(_scratch.Inside("longsourcefoldername"));

        (int exitCode, _, string stdErr) = _scratch.PakkoIn("jw", "a", "-sdel", _scratch.Inside("o.zip"), shortPath);

        Read(@"longsourcefoldername\a.txt").Should().Be("A", $"{shortPath} holds the current folder (exit {exitCode}, stderr: {stdErr})");
        exitCode.Should().Be(7, stdErr);
    }

    [Theory]
    [InlineData("C:.")]
    [InlineData("C:..")]
    [InlineData("../work")]
    public void Sdel_DriveRelativeOrSlashSpellingOfTheCurrentFolder_IsRefused(string source)
    {
        _scratch.Root[0].Should().Be('C', "harness: the drive-relative spellings name drive C:");
        _scratch.WriteFile(@"src\work\w.txt", "W");
        _scratch.WriteFile(@"src\a.txt", "A");

        (int exitCode, _, string stdErr) = _scratch.PakkoIn(@"src\work", "a", "-sdel", @"..\..\o.zip", source);

        Read(@"src\work\w.txt").Should().Be("W", $"'{source}' is the current folder or holds it (exit {exitCode}, stderr: {stdErr})");
        Read(@"src\a.txt").Should().Be("A");
        exitCode.Should().Be(7, stdErr);
    }

    // CLI.md: "`.` names the current folder's items, which go, while the folder stays"; "C:" and
    // "./" are the same folder as ".", so the same rule: everything deleted is in the archive.
    [Theory]
    [InlineData("C:")]
    [InlineData("./")]
    public void Sdel_DriveRelativeOrSlashDot_IsLikeDot_ItemsGoIntoTheArchiveFolderStays(string source)
    {
        _scratch.Root[0].Should().Be('C', "harness: the drive-relative spelling names drive C:");
        _scratch.WriteFile(@"src\work\w.txt", "W");

        (int exitCode, _, string stdErr) = _scratch.PakkoIn(@"src\work", "a", "-sdel", @"..\..\o.zip", source);

        exitCode.Should().Be(0, stdErr);
        Directory.Exists(_scratch.Inside(@"src\work")).Should().BeTrue();
        using ZipArchive zip = ZipFile.OpenRead(_scratch.Inside("o.zip"));
        zip.Entries.Should().Contain(e => e.Name == "w.txt");
    }

    // The same source twice, once through a junction: archived, and the files behind the
    // junction are the source's own files - they may go only once, and only from the real path.
    [Fact]
    public void Sdel_SourceGivenAlsoThroughAJunction_ArchiveHoldsEverythingDeleted()
    {
        _scratch.WriteFile(@"src\a.txt", "A");
        _scratch.Junction("j", "src");

        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "-sdel", "o.zip", "src", "j");

        if (!File.Exists(_scratch.Inside(@"src\a.txt")))
        {
            using ZipArchive zip = ZipFile.OpenRead(_scratch.Inside("o.zip"));
            zip.Entries.Should().Contain(e => e.Name == "a.txt", $"a.txt was deleted, so it must be in the archive (exit {exitCode}, stderr: {stdErr})");
        }
        Directory.Exists(_scratch.Inside("j")).Should().BeTrue("CLI.md: a link is never deleted");
    }

    // In process, never through the exe: a failed refusal here would archive and delete a whole
    // volume. CLI.md: "Refused with exit 7 before anything is written: a volume root".
    [Fact]
    public void SdelRefusal_VolumeRootThroughItsGuidPath_IsRefused()
    {
        string volume = VolumeGuidPath.Of(Path.GetPathRoot(Path.GetTempPath())!);
        Directory.Exists(volume).Should().BeTrue("harness: the volume GUID path opens the volume");

        Archiver.CLI.CliSourceDeleter.Refusal([volume], _scratch.Root).Should().NotBeNull(
            $"{volume} is the volume root of {Path.GetPathRoot(Path.GetTempPath())}");
    }

    [RequiresAdminShareFact]
    public void SdelRefusal_VolumeRootThroughTheAdminShare_IsRefused()
    {
        string share = @"\\localhost\" + Path.GetTempPath()[0] + @"$\";

        Archiver.CLI.CliSourceDeleter.Refusal([share], _scratch.Root).Should().NotBeNull($"{share} is a volume root");
        Archiver.CLI.CliSourceDeleter.Refusal([@"\\?\UNC\localhost\" + Path.GetTempPath()[0] + @"$\"], _scratch.Root).Should().NotBeNull();
    }

    [RequiresAdminShareFact]
    public void SdelRefusal_CurrentFolderThroughTheAdminShare_IsRefused()
    {
        string share = @"\\localhost\" + _scratch.Root[0] + "$" + _scratch.Root[2..];

        Archiver.CLI.CliSourceDeleter.Refusal([share], _scratch.Root).Should().NotBeNull($"{share} is the current folder");
    }

    // In process, same reason: GLOBALROOT and \\.\ spellings of the volume root.
    [Fact]
    public void SdelRefusal_VolumeRootThroughGlobalRootOrDotPrefix_IsRefused()
    {
        string drive = Path.GetPathRoot(Path.GetTempPath())!;
        string device = VolumeGuidPath.DeviceOf(drive.TrimEnd('\\'));
        string globalRoot = @"\\?\GLOBALROOT" + device + @"\";
        string dotRoot = @"\\.\" + drive;

        Archiver.CLI.CliSourceDeleter.Refusal([dotRoot], _scratch.Root).Should().NotBeNull($"{dotRoot} is the volume root");
        Archiver.CLI.CliSourceDeleter.Refusal([globalRoot], _scratch.Root).Should().NotBeNull($"{globalRoot} is the volume root");
    }

    [Fact]
    public void SdelRefusal_CurrentFolderThroughDotPrefix_IsRefused()
    {
        string dotted = @"\\.\" + _scratch.Root;
        Directory.Exists(dotted).Should().BeTrue("harness");

        Archiver.CLI.CliSourceDeleter.Refusal([dotted], _scratch.Root).Should().NotBeNull($"{dotted} is the current folder");
    }

    // CLI.md: "neither is a source whose path passes a link (j\t.txt ...)". Spelled through the
    // volume GUID path or the admin share, the junction check compares the spelling with itself.
    [Fact]
    public void Sdel_SourceThroughAJunction_SpelledThroughTheVolumeGuidPath_FileBehindItStays()
    {
        string target = _scratch.WriteFile(@"target\t.txt", "T");
        _scratch.Junction("j", "target");
        string viaJunction = _scratch.Inside(@"j\t.txt");
        string spelled = VolumeGuidPath.Of(Path.GetPathRoot(viaJunction)!) + viaJunction[3..];
        File.Exists(spelled).Should().BeTrue("harness");

        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "-sdel", "o.zip", spelled);

        File.Exists(target).Should().BeTrue($"{spelled} passes the junction j; -sdel must not delete what is behind it (exit {exitCode}, stderr: {stdErr})");
    }

    [RequiresAdminShareFact]
    public void Sdel_SourceThroughAJunction_SpelledThroughTheAdminShare_FileBehindItStays()
    {
        string target = _scratch.WriteFile(@"target\t.txt", "T");
        _scratch.Junction("j", "target");
        string viaJunction = _scratch.Inside(@"j\t.txt");
        string spelled = @"\\localhost\" + viaJunction[0] + "$" + viaJunction[2..];

        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "-sdel", "o.zip", spelled);

        File.Exists(target).Should().BeTrue($"{spelled} passes the junction j; -sdel must not delete what is behind it (exit {exitCode}, stderr: {stdErr})");
    }

    // =====================================================================================
    // 7./8. Only links; \\?\ archives with wildcards and filters
    // =====================================================================================

    [RequiresSymlinkFact]
    public void OnlyLinks_JunctionAndFileSymlinkTogether_IsNothingToArchiveExitTwo()
    {
        _scratch.WriteFile(@"target\t.txt", "T");
        _scratch.Junction("j", "target");
        File.CreateSymbolicLink(_scratch.Inside("s.txt"), _scratch.Inside(@"target\t.txt"));

        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "o.zip", "j", "s.txt");

        exitCode.Should().Be(2, stdErr);
        File.Exists(_scratch.Inside("o.zip")).Should().BeFalse();
    }

    [Fact]
    public void OnlyLinks_FolderHoldingOnlyAJunction_IsNotASilentEmptyArchive()
    {
        _scratch.WriteFile(@"target\t.txt", "T");
        _scratch.Folder("holder");
        _scratch.Junction(@"holder\j", "target");

        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "o.zip", "holder");

        exitCode.Should().NotBe(0, $"the junction was left out; stderr: {stdErr}");
    }

    [Theory]
    [InlineData("x")]
    [InlineData("t")]
    [InlineData("l")]
    public void LongPathPrefixArchive_WithARealWildcard_OpensTheMatches(string command)
    {
        _scratch.Zip("one.zip", ("a.txt", "A"));
        _scratch.Zip("two.zip", ("b.txt", "B"));
        string mask = @"\\?\" + _scratch.Inside("*.zip");
        string[] args = command == "x" ? [command, mask, "-oout"] : [command, mask];

        (int exitCode, _, string stdErr) = _scratch.Pakko(args);

        exitCode.Should().Be(0, stdErr);
        if (command == "x")
            Contents("out").Should().Equal("A", "B");
    }

    [Fact]
    public void LongPathPrefixArchive_WithAi_OpensBoth()
    {
        _scratch.Zip("one.zip", ("a.txt", "A"));
        _scratch.Zip("two.zip", ("b.txt", "B"));

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", @"\\?\" + _scratch.Inside("one.zip"), @"-ai!\\?\" + _scratch.Inside("two.zip"), "-oout");

        exitCode.Should().Be(0, stdErr);
        Contents("out").Should().Equal("A", "B");
    }

    // 7za 26.02 measured: "x *.zip -ax!\\?\<dir>\two.zip" extracts both, exit 0 (the -ax
    // spelling does not match the scanned name). Pakko leaves two.zip out, as typed: a searched
    // mask is read without the prefix (T-F419 E), and doing less than 7-Zip here loses nothing.
    [Fact]
    public void LongPathPrefixArchive_WithAx_LeavesThatArchiveOut()
    {
        _scratch.Zip("one.zip", ("a.txt", "A"));
        _scratch.Zip("two.zip", ("b.txt", "B"));

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", "*.zip", @"-ax!\\?\" + _scratch.Inside("two.zip"), "-oout");

        exitCode.Should().Be(0, stdErr);
        Contents("out").Should().Equal("A");
    }

    // =====================================================================================
    // 9. -o as Windows reads it
    // =====================================================================================

    [Theory]
    [InlineData("out. ", "out")]
    [InlineData("out .", "out")]
    [InlineData("out..", "out")]
    [InlineData(@"..\x ", @"..\x")]
    [InlineData(@"..\x.", @"..\x")]
    public void Output_TrailingDotsAndSpaces_LandWhereWindowsReadsThem(string output, string expected)
    {
        _scratch.Zip("arc.zip", ("a.txt", "A"));
        _scratch.Folder("cwd");

        (int exitCode, _, string stdErr) = _scratch.PakkoIn("cwd", "x", _scratch.Inside("arc.zip"), "-o" + output);

        exitCode.Should().Be(0, stdErr);
        string landed = Path.GetFullPath(Path.Combine(_scratch.Inside("cwd"), expected, "a.txt"));
        File.ReadAllText(landed).Should().Be("A");
        AllNames().Should().NotContain(n => n.EndsWith(' ') || n.EndsWith('.'), "no folder of a name Explorer cannot open");
    }

    // A blank -o is the current folder as Windows reads it.
    [Theory]
    [InlineData(".")]
    [InlineData(". ")]
    public void Output_BlankOrDot_IsTheCurrentFolder(string output)
    {
        _scratch.Zip("arc.zip", ("a.txt", "A"));
        _scratch.Folder("cwd");

        (int exitCode, _, string stdErr) = _scratch.PakkoIn("cwd", "x", _scratch.Inside("arc.zip"), "-o" + output);

        exitCode.Should().Be(0, stdErr);
        Read(@"cwd\a.txt").Should().Be("A");
        AllNames().Should().NotContain(n => n.EndsWith(' ') || n == ".");
    }

    // 7za 26.02 measured: -o" " writes a folder named " " (exit 0), which Explorer cannot open.
    // Pakko refuses it (exit 2) and writes nothing: no byte lost, no false success. Pinned. The
    // reason is Pakko's own, not .NET's "The path is empty. (Parameter 'path')" (T-F419 triage).
    [Fact]
    public void Output_SingleSpace_IsRefusedAndWritesNothing()
    {
        _scratch.Zip("arc.zip", ("a.txt", "A"));
        _scratch.Folder("cwd");

        (int exitCode, _, string stdErr) = _scratch.PakkoIn("cwd", "x", _scratch.Inside("arc.zip"), "-o ");

        exitCode.Should().Be(2, stdErr);
        stdErr.Should().Contain("is not a folder name Windows can use").And.NotContain("Parameter");
        _scratch.FilesUnder("cwd").Should().BeEmpty();
        AllNames().Should().NotContain(" ");
    }

    // CLI.md, -spe: "the last part of -o as typed" (7-Zip's SplitPathToParts_Smart). -o"out "
    // extracts into out, but its typed last part is "out ", which no root folder has: the root
    // stays (7za writes "out \out\a.txt"). Pinned as documented.
    [Fact]
    public void Output_TrailingSpace_WithSpe_ComparesTheTypedName_RootStays()
    {
        _scratch.Zip("arc.zip", ("out/a.txt", "A"));

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", "arc.zip", "-oout ", "-spe");

        exitCode.Should().Be(0, stdErr);
        _scratch.FilesUnder("out").Should().Equal(@"out\a.txt");
    }

    // CLI.md: "a \\?\ path stays as typed".
    [Fact]
    public void Output_LongPathPrefixWithTrailingSpace_StaysLiteral()
    {
        _scratch.Zip("arc.zip", ("a.txt", "A"));

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", "arc.zip", @"-o\\?\" + _scratch.Inside("out") + " ");

        exitCode.Should().Be(0, stdErr);
        File.ReadAllText(@"\\?\" + _scratch.Inside("out") + @" \a.txt").Should().Be("A");
    }

    // =====================================================================================
    // 10. What is in the way at commit
    // =====================================================================================

    [Theory]
    [InlineData(FileAttributes.ReadOnly | FileAttributes.Hidden)]
    [InlineData(FileAttributes.ReadOnly | FileAttributes.System)]
    [InlineData(FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System)]
    [InlineData(FileAttributes.Hidden)]
    public void Zip_AttributedDestinationFile_Aoa_IsReplaced(FileAttributes attributes)
    {
        _scratch.Zip("arc.zip", ("a.txt", "NEW"));
        string target = _scratch.WriteFile(@"out\a.txt", "OLD");
        File.SetAttributes(target, attributes);
        try
        {
            (int exitCode, _, string stdErr) = _scratch.Pakko("x", "arc.zip", "-oout", "-aoa");

            exitCode.Should().Be(0, $"7-Zip replaces it; stderr: {stdErr}");
            File.ReadAllText(target).Should().Be("NEW");
        }
        finally
        {
            File.SetAttributes(target, FileAttributes.Normal);
        }
    }

    // The read-only attribute is cleared before the move; when the move then fails (the file is
    // held open), the user's file must keep its attribute - nothing was replaced.
    [Fact]
    public void Zip_ReadOnlyDestinationFileHeldOpen_Aoa_KeepsItsReadOnlyAttribute()
    {
        _scratch.Zip("arc.zip", ("a.txt", "NEW"));
        string target = _scratch.WriteFile(@"out\a.txt", "OLD");
        File.SetAttributes(target, FileAttributes.ReadOnly);
        try
        {
            int exitCode;
            string stdErr;
            using (new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read))
                (exitCode, _, stdErr) = _scratch.Pakko("x", "arc.zip", "-oout", "-aoa");

            File.ReadAllText(target).Should().Be("OLD");
            exitCode.Should().Be(2, stdErr);
            File.GetAttributes(target).Should().HaveFlag(FileAttributes.ReadOnly, "the file was not replaced, so it must stay as the user left it");
        }
        finally
        {
            File.SetAttributes(target, FileAttributes.Normal);
        }
    }

    [Fact]
    public void Aot_FolderInTheWayHeldOpen_ContentStaysWhereItWas()
    {
        _scratch.Zip("arc.zip", ("a.txt", "NEW"));
        string inner = _scratch.WriteFile(@"out\a.txt\inner.txt", "INNER");

        int exitCode;
        string stdErr;
        using (new FileStream(inner, FileMode.Open, FileAccess.Read, FileShare.Read))
            (exitCode, _, stdErr) = _scratch.Pakko("x", "arc.zip", "-oout", "-aot");

        File.ReadAllText(inner).Should().Be("INNER", $"exit {exitCode}, stderr: {stdErr}");
        exitCode.Should().Be(2, stdErr);
        stdErr.Should().Contain("a.txt");
    }

    [Fact]
    public void Aot_FolderInTheWay_NumberedNamesTakenByFileAndFolder_NothingIsOverwritten()
    {
        _scratch.Zip("arc.zip", ("a.txt", "NEW"));
        _scratch.WriteFile(@"out\a.txt\inner.txt", "INNER");
        _scratch.WriteFile(@"out\a (1).txt", "ONE");
        _scratch.WriteFile(@"out\a (2).txt\two.txt", "TWO");

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", "arc.zip", "-oout", "-aot");

        exitCode.Should().Be(0, stdErr);
        Read(@"out\a (1).txt").Should().Be("ONE");
        Read(@"out\a (2).txt\two.txt").Should().Be("TWO");
        Read(@"out\a (3).txt\inner.txt").Should().Be("INNER");
        Read(@"out\a.txt").Should().Be("NEW");
    }

    [Fact]
    public void TarGz_ReadOnlyDestinationFile_Aoa_IsReplaced()
    {
        TarGz("arc.tar.gz", ("a.txt", "NEW"));
        string target = _scratch.WriteFile(@"out\a.txt", "OLD");
        File.SetAttributes(target, FileAttributes.ReadOnly);
        try
        {
            (int exitCode, _, string stdErr) = _scratch.Pakko("x", "arc.tar.gz", "-oout", "-aoa");

            exitCode.Should().Be(0, stdErr);
            File.ReadAllText(target).Should().Be("NEW");
        }
        finally
        {
            File.SetAttributes(target, FileAttributes.Normal);
        }
    }

    [Fact]
    public void TarGz_FolderInTheWay_Aoa_IsExitTwoAndSaysSo()
    {
        TarGz("arc.tar.gz", ("a.txt", "NEW"));
        _scratch.WriteFile(@"out\a.txt\inner.txt", "INNER");

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", "arc.tar.gz", "-oout", "-aoa");

        Read(@"out\a.txt\inner.txt").Should().Be("INNER");
        exitCode.Should().Be(2, stdErr);
        stdErr.Should().Contain("a folder of that name is in the way").And.NotContain("locked");
    }

    [Fact]
    public void TarGz_FolderInTheWay_Aot_IsRenamedContentKept()
    {
        TarGz("arc.tar.gz", ("a.txt", "NEW"));
        _scratch.WriteFile(@"out\a.txt\inner.txt", "INNER");

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", "arc.tar.gz", "-oout", "-aot");

        exitCode.Should().Be(0, stdErr);
        Read(@"out\a.txt").Should().Be("NEW");
        Read(@"out\a (1).txt\inner.txt").Should().Be("INNER");
    }

    // =====================================================================================
    // 11. A switch on the wrong command names itself
    // =====================================================================================

    [Theory]
    [InlineData("l", "-aot")]
    [InlineData("t", "-spe")]
    [InlineData("l", "-sdel")]
    [InlineData("x", "-sdel")]
    [InlineData("x", "-rr")]
    [InlineData("x", "-sae")]
    [InlineData("x", "-w")]
    [InlineData("t", "-so")]
    [InlineData("a", "-spe")]
    [InlineData("a", "-aou")]
    public void SwitchOnTheWrongCommand_IsExitSevenAndNamesTheSwitch(string command, string wrongSwitch)
    {
        _scratch.Zip("arc.zip", ("a.txt", "A"));
        _scratch.WriteFile("b.txt", "B");
        string[] args = command == "a" ? [command, wrongSwitch, "o.zip", "b.txt"] : [command, "arc.zip", wrongSwitch];

        (int exitCode, _, string stdErr) = _scratch.Pakko(args);

        exitCode.Should().Be(7, stdErr);
        stdErr.Should().Contain(wrongSwitch, "the message names the switch that does not belong");
        File.Exists(_scratch.Inside("o.zip")).Should().BeFalse();
        File.Exists(_scratch.Inside("a.txt")).Should().BeFalse();
    }

    // --- process helpers ---

    private (int ExitCode, int BytesRead, string StdErr) StdoutReadingOnly(int bytes, params string[] args)
    {
        var startInfo = new ProcessStartInfo(CliProcessRunner.ExePath)
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = _scratch.Root,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (string arg in args)
            startInfo.ArgumentList.Add(arg);
        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("pakko did not start");
        process.StandardInput.Close();
        Task<string> stdErr = process.StandardError.ReadToEndAsync();
        byte[] buffer = new byte[Math.Max(bytes, 1)];
        int read = bytes == 0 ? 0 : process.StandardOutput.BaseStream.ReadAtLeast(buffer, bytes, throwOnEndOfStream: false);
        process.StandardOutput.Close();
        process.WaitForExit();
        return (process.ExitCode, read, stdErr.GetAwaiter().GetResult());
    }

    // =====================================================================================
    // T-F420: a character Windows refuses in a name becomes '_' (7-Zip, libarchive), one rule
    // for both engines; a name that only lands on another entry's path through it is kept apart.
    // =====================================================================================

    [Theory]
    [InlineData("zip", "-y")]
    [InlineData("zip", "-aou")]
    [InlineData("tar.gz", "-y")]
    [InlineData("tar.gz", "-aou")]
    public void NameWithARefusedCharacter_BesideItsUnderscoreTwin_BothKept(string format, string overwrite)
    {
        (string Name, string Content)[] entries = [("a?b.txt", "QUESTION"), ("a_b.txt", "UNDERSCORE")];
        string archive = format == "zip" ? _scratch.Zip("arc.zip", entries) : TarGz("arc.tar.gz", entries);

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", archive, "-oout", overwrite);

        exitCode.Should().Be(0, stdErr);
        Contents("out").Should().Equal(["QUESTION", "UNDERSCORE"], $"files: {string.Join(", ", _scratch.FilesUnder("out"))}");
    }

    [Theory]
    [InlineData("zip")]
    [InlineData("tar.gz")]
    public void NameWithRefusedCharacters_IsWrittenWithUnderscores(string format)
    {
        (string Name, string Content)[] entries = [("What?.txt", "Q"), ("d|e/x<y>.txt", "P")];
        string archive = format == "zip" ? _scratch.Zip("arc.zip", entries) : TarGz("arc.tar.gz", entries);

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", archive, "-oout");

        exitCode.Should().Be(0, stdErr);
        File.ReadAllText(_scratch.Inside(@"out\What_.txt")).Should().Be("Q");
        File.ReadAllText(_scratch.Inside(@"out\d_e\x_y_.txt")).Should().Be("P");
    }

    // 7-Zip's k_EmptyReplaceName: a part that is only dots or spaces is not dropped into its parent.
    [Fact]
    public void Zip_PartOfOnlyDots_IsAnUnderscoreFolder()
    {
        _scratch.Zip("arc.zip", ("a/.../b.txt", "B"), ("a/b.txt", "A"));

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", "arc.zip", "-oout", "-y");

        exitCode.Should().Be(0, stdErr);
        File.ReadAllText(_scratch.Inside(@"out\a\_\b.txt")).Should().Be("B");
        File.ReadAllText(_scratch.Inside(@"out\a\b.txt")).Should().Be("A");
    }

    private (int ExitCode, string StdErr) RunThroughCmd(string commandLine)
    {
        var startInfo = new ProcessStartInfo(@"C:\Windows\System32\cmd.exe")
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = _scratch.Root,
            RedirectStandardInput = true, RedirectStandardError = true,
            Arguments = "/d /s /c \"" + commandLine + "\"",
        };
        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("cmd did not start");
        process.StandardInput.Close();
        string stdErr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdErr);
    }
}

/// <summary>GetVolumeNameForVolumeMountPointW: "\\?\Volume{GUID}\" for a drive root.</summary>
internal static partial class VolumeGuidPath
{
    public static string Of(string driveRoot)
    {
        char[] buffer = new char[64];
        if (!GetVolumeNameForVolumeMountPointW(driveRoot, buffer, (uint)buffer.Length))
            throw new IOException($"GetVolumeNameForVolumeMountPointW failed for {driveRoot}: {Marshal.GetLastPInvokeError()}");
        return new string(buffer).TrimEnd('\0');
    }

    /// <summary>QueryDosDeviceW: "\Device\HarddiskVolumeN" for "C:".</summary>
    public static string DeviceOf(string drive)
    {
        char[] buffer = new char[1024];
        uint length = QueryDosDeviceW(drive, buffer, (uint)buffer.Length);
        if (length == 0)
            throw new IOException($"QueryDosDeviceW failed for {drive}: {Marshal.GetLastPInvokeError()}");
        return new string(buffer, 0, (int)length).Split('\0')[0];
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial uint QueryDosDeviceW(string deviceName, [Out] char[] targetPath, uint max);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetVolumeNameForVolumeMountPointW(string mountPoint, [Out] char[] volumeName, uint bufferLength);
}

/// <summary><see cref="RequiresSymlinkFactAttribute"/> for a theory.</summary>
public sealed class RequiresSymlinkTheoryAttribute : TheoryAttribute
{
    public RequiresSymlinkTheoryAttribute() => Skip = new RequiresSymlinkFactAttribute().Skip;
}

/// <summary><see cref="RequiresShortNamesFactAttribute"/> for a theory.</summary>
public sealed class RequiresShortNamesTheoryAttribute : TheoryAttribute
{
    public RequiresShortNamesTheoryAttribute() => Skip = new RequiresShortNamesFactAttribute().Skip;
}

/// <summary><see cref="RequiresAdminShareFactAttribute"/> for a theory.</summary>
public sealed class RequiresAdminShareTheoryAttribute : TheoryAttribute
{
    public RequiresAdminShareTheoryAttribute() => Skip = new RequiresAdminShareFactAttribute().Skip;
}
