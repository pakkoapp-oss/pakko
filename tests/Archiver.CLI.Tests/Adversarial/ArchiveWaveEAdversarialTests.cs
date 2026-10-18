using System.IO.Compression;
using System.Security.Cryptography;
using Archiver.CLI.Tests.Subprocess;
using FluentAssertions;

namespace Archiver.CLI.Tests.Adversarial;

/// <summary>
/// T-F419 red team, wave E's archiving switches: -sse, -ssw, -stl, -w, -mmt, and the declined -ssp.
/// An archive the run did not finish never replaces one that was there; a refused line writes nothing.
/// </summary>
public sealed class ArchiveWaveEAdversarialTests : IDisposable
{
    private static readonly byte[] OldArchive = "OLD ARCHIVE BYTES"u8.ToArray();
    private readonly AdversarialScratch _scratch = new();

    public ArchiveWaveEAdversarialTests()
    {
        _scratch.WriteFile(@"src\a.txt", "A");
        _scratch.WriteFile(@"src\b.txt", "B");
        _scratch.WriteFile(@"src\sub\s.txt", "S");
    }

    public void Dispose() => _scratch.Dispose();

    private static string[] EntryNames(string zipPath)
    {
        using ZipArchive archive = ZipFile.OpenRead(zipPath);
        return [.. archive.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal)];
    }

    private string[] RootListing() =>
        [.. Directory.EnumerateFileSystemEntries(_scratch.Root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(_scratch.Root, path) + "=" + (File.Exists(path) ? Convert.ToHexString(File.ReadAllBytes(path)) : "dir"))
            .Order(StringComparer.Ordinal)];

    // --- -sse ---

    // CLI.md: "an existing one it would replace stays byte for byte".
    [Theory]
    [InlineData("lock")]
    [InlineData("acl")]
    public void Sse_UnreadableSource_WithYOverAnExistingArchive_OldArchiveSurvives(string how)
    {
        File.WriteAllBytes(_scratch.Inside("o.zip"), OldArchive);
        int exitCode;
        string stdErr;
        if (how == "acl")
        {
            _scratch.Deny(@"src\b.txt", "R");
            (exitCode, _, stdErr) = _scratch.Pakko("a", "-sse", "-y", "o.zip", "src");
        }
        else
        {
            using (new FileStream(_scratch.Inside(@"src\b.txt"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                (exitCode, _, stdErr) = _scratch.Pakko("a", "-sse", "-y", "o.zip", "src");
        }

        File.ReadAllBytes(_scratch.Inside("o.zip")).Should().Equal(OldArchive, stdErr);
        exitCode.Should().Be(2, stdErr);
        stdErr.Should().Contain("b.txt").And.Contain("no archive written: -sse stops at a file that cannot be read");
        Directory.GetFiles(_scratch.Root).Select(Path.GetFileName).Should().Equal(["o.zip"], "no temporary archive is left behind");
    }

    [RequiresTarExe]
    public void Sse_Tar_UnreadableSource_WithYOverAnExistingArchive_OldArchiveSurvives()
    {
        File.WriteAllBytes(_scratch.Inside("o.tar"), OldArchive);
        int exitCode;
        string stdErr;
        using (new FileStream(_scratch.Inside(@"src\b.txt"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            (exitCode, _, stdErr) = _scratch.Pakko("a", "-sse", "-y", "-ttar", "o.tar", "src");

        File.ReadAllBytes(_scratch.Inside("o.tar")).Should().Equal(OldArchive, stdErr);
        exitCode.Should().Be(2, stdErr);
    }

    // CLI.md: "A missing source is a warning before anything is read and not covered".
    [Fact]
    public void Sse_MissingSource_IsAWarningAndTheRestIsArchived()
    {
        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "-sse", "o.zip", "src", "missing.txt");

        exitCode.Should().Be(1, stdErr);
        EntryNames(_scratch.Inside("o.zip")).Should().Contain(["src/a.txt", "src/b.txt", "src/sub/s.txt"]);
    }

    [Fact]
    public void Sse_EverythingReadable_IsAnOrdinaryArchive()
    {
        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "-sse", "o.zip", "src");

        exitCode.Should().Be(0, stdErr);
        EntryNames(_scratch.Inside("o.zip")).Should().Contain(["src/a.txt", "src/b.txt", "src/sub/s.txt"]);
    }

    // --- -ssw ---

    // A program holds b.txt open for writing and lets others read it.
    [Fact]
    public void Ssw_FileOpenForWriting_IsArchivedWithItsBytes_WithoutItIsAnError()
    {
        int without;
        int with;
        string withErr;
        using (var writer = new FileStream(_scratch.Inside(@"src\b.txt"), FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
        {
            (without, _, _) = _scratch.Pakko("a", "plain.zip", "src");
            (with, _, withErr) = _scratch.Pakko("a", "-ssw", "shared.zip", "src");
        }

        without.Should().Be(2, "CLI.md: without -ssw such a file is an error (Pakko: exit 2)");
        with.Should().Be(0, withErr);
        using ZipArchive archive = ZipFile.OpenRead(_scratch.Inside("shared.zip"));
        using var reader = new StreamReader(archive.GetEntry("src/b.txt")!.Open());
        reader.ReadToEnd().Should().Be("B");
    }

    [Fact]
    public void Ssw_DoesNotReadAFileLockedAgainstReading()
    {
        int exitCode;
        using (new FileStream(_scratch.Inside(@"src\b.txt"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            (exitCode, _, _) = _scratch.Pakko("a", "-ssw", "o.zip", "src");

        exitCode.Should().Be(2);
    }

    // --- -stl ---

    private void SetTimes(DateTime a, DateTime b, DateTime sub, DateTime s, DateTime src)
    {
        File.SetLastWriteTimeUtc(_scratch.Inside(@"src\a.txt"), a);
        File.SetLastWriteTimeUtc(_scratch.Inside(@"src\b.txt"), b);
        File.SetLastWriteTimeUtc(_scratch.Inside(@"src\sub\s.txt"), s);
        Directory.SetLastWriteTimeUtc(_scratch.Inside(@"src\sub"), sub);
        Directory.SetLastWriteTimeUtc(_scratch.Inside("src"), src);
    }

    private static DateTime Utc(int year) => new(year, 3, 4, 5, 6, 8, DateTimeKind.Utc);

    // CLI.md: "the newest modification time among the files and folders it holds (7za counts folders too)".
    [Theory]
    [InlineData(2001, 2002, 2003, 2004, 2010, 2010)]
    [InlineData(2001, 2099, 2003, 2004, 2005, 2099)]
    [InlineData(2001, 2002, 2015, 2004, 2005, 2015)]
    [InlineData(1981, 1982, 1983, 1984, 1985, 1985)]
    public void Stl_ArchiveTakesTheNewestTime(int a, int b, int sub, int s, int src, int expected)
    {
        SetTimes(Utc(a), Utc(b), Utc(sub), Utc(s), Utc(src));

        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "-stl", "o.zip", "src");

        exitCode.Should().Be(0, stdErr);
        File.GetLastWriteTimeUtc(_scratch.Inside("o.zip")).Should().Be(Utc(expected));
    }

    // CLI.md: "what a filter leaves out ... do not count".
    [Fact]
    public void Stl_NewestFileLeftOutByAFilter_DoesNotCount()
    {
        SetTimes(Utc(2001), Utc(2099), Utc(2003), Utc(2004), Utc(2005));

        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "-stl", "o.zip", "src", "-xr!b.txt");

        exitCode.Should().Be(0, stdErr);
        EntryNames(_scratch.Inside("o.zip")).Should().NotContain("src/b.txt");
        File.GetLastWriteTimeUtc(_scratch.Inside("o.zip")).Should().Be(Utc(2005));
    }

    // CLI.md: "what is behind a link do not count".
    [Fact]
    public void Stl_NewerFilesBehindAJunction_DoNotCount()
    {
        _scratch.WriteFile(@"other\n.txt", "N");
        File.SetLastWriteTimeUtc(_scratch.Inside(@"other\n.txt"), Utc(2098));
        _scratch.Junction(@"src\j", "other");
        SetTimes(Utc(2001), Utc(2002), Utc(2003), Utc(2004), Utc(2005));

        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "-stl", "o.zip", "src");

        exitCode.Should().BeLessThan(2, stdErr);
        File.GetLastWriteTimeUtc(_scratch.Inside("o.zip")).Should().BeBefore(Utc(2090));
    }

    [Fact]
    public void Stl_EmptyFolder_TakesTheFoldersTime()
    {
        _scratch.Folder("empty");
        Directory.SetLastWriteTimeUtc(_scratch.Inside("empty"), Utc(2004));

        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "-stl", "o.zip", "empty");

        exitCode.Should().Be(0, stdErr);
        File.GetLastWriteTimeUtc(_scratch.Inside("o.zip")).Should().Be(Utc(2004));
    }

    [RequiresTarExe]
    public void Stl_Tar_TakesTheNewestTime()
    {
        SetTimes(Utc(2001), Utc(2002), Utc(2003), Utc(2004), Utc(2010));

        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "-stl", "-ttar", "o.tar", "src");

        exitCode.Should().Be(0, stdErr);
        File.GetLastWriteTimeUtc(_scratch.Inside("o.tar")).Should().Be(Utc(2010));
    }

    [Theory]
    [InlineData("x", "o.zip", "-oout")]
    [InlineData("t", "o.zip")]
    public void Stl_OnAnotherCommand_IsExitSeven(params string[] args)
    {
        _scratch.Pakko("a", "o.zip", "src").ExitCode.Should().Be(0);
        string[] before = RootListing();

        (int exitCode, _, _) = _scratch.Pakko([.. args, "-stl"]);

        exitCode.Should().Be(7);
        RootListing().Should().Equal(before);
    }

    // --- -w ---

    [Theory]
    [InlineData("-wnothere")]
    [InlineData("-wafile")]
    [InlineData("-w\"")]
    [InlineData(@"-wnothere\deeper\")]
    public void W_FolderThatIsNotThere_IsExitTwoAndWritesNothing(string working)
    {
        File.WriteAllBytes(_scratch.Inside("o.zip"), OldArchive);
        _scratch.WriteFile("afile", "F");
        string[] before = RootListing();

        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "-y", working, "o.zip", "src");

        exitCode.Should().Be(2, stdErr);
        RootListing().Should().Equal(before, "CLI.md: exit 2 before anything is written");
    }

    [Theory]
    [InlineData("-w")]
    [InlineData("-wwork")]
    [InlineData(@"-wwork\")]
    [InlineData("-wWORK/")]
    [InlineData("-wro")]
    [InlineData("-wjwork")]
    public void W_ExistingFolder_ArchiveIsWrittenAndTheFolderStaysEmpty(string working)
    {
        _scratch.Folder("work");
        var ro = new DirectoryInfo(_scratch.Folder("ro"));
        ro.Attributes |= FileAttributes.ReadOnly;
        _scratch.Junction("jwork", "work");
        File.WriteAllBytes(_scratch.Inside("o.zip"), OldArchive);

        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "-y", working, "o.zip", "src");

        exitCode.Should().Be(0, stdErr);
        EntryNames(_scratch.Inside("o.zip")).Should().Contain(["src/a.txt", "src/b.txt", "src/sub/s.txt"]);
        Directory.EnumerateFileSystemEntries(_scratch.Inside("work")).Should().BeEmpty();
        Directory.EnumerateFileSystemEntries(_scratch.Inside("ro")).Should().BeEmpty();
        ro.Attributes &= ~FileAttributes.ReadOnly;
    }

    [Theory]
    [InlineData("-wwork", "-wwork")]
    [InlineData("-w", "-wwork")]
    public void W_Twice_IsExitSeven(string first, string second)
    {
        _scratch.Folder("work");

        (int exitCode, _, _) = _scratch.Pakko("a", first, second, "o.zip", "src");

        exitCode.Should().Be(7);
        File.Exists(_scratch.Inside("o.zip")).Should().BeFalse();
    }

    [Fact]
    public void W_OnExtract_IsExitSeven()
    {
        _scratch.Pakko("a", "o.zip", "src").ExitCode.Should().Be(0);
        _scratch.Folder("work");

        (int exitCode, _, _) = _scratch.Pakko("x", "o.zip", "-oout", "-wwork");

        exitCode.Should().Be(7);
        Directory.Exists(_scratch.Inside("out")).Should().BeFalse();
    }

    // --- -mmt ---

    [Theory]
    [InlineData("-mmt4294967296")]
    [InlineData("-mmt99999999999999999999")]
    [InlineData("-mmt-1")]
    [InlineData("-mmt=-1")]
    [InlineData("-mmt+2")]
    [InlineData("-mmt=+2")]
    [InlineData("-mmtON")]
    [InlineData("-mmtoff")]
    [InlineData("-mmt=x")]
    [InlineData("-mmt0x10")]
    [InlineData("-mmt=4294967296")]
    [InlineData("-mmt2.5")]
    [InlineData("-mmt=on=off")]
    public void Mmt_HostileValue_IsExitSevenAndWritesNothing(string value)
    {
        (int exitCode, _, string stdErr) = _scratch.Pakko("a", value, "o.zip", "src");

        exitCode.Should().Be(7, stdErr);
        File.Exists(_scratch.Inside("o.zip")).Should().BeFalse();
        stdErr.Should().NotContain("Unexpected error");
    }

    // CLI.md: "the archive is the same bytes either way".
    [Fact]
    public void Mmt_AnyThreadCount_WritesTheSameBytes()
    {
        for (int i = 0; i < 24; i++)
            File.WriteAllBytes(_scratch.Inside($@"src\r{i}.bin"), RandomNumberGenerator.GetBytes(64 * 1024 + i));
        string[] values = ["-mmt1", "-mmt16", "-mmt=off", "-mmt", "-mmt0", "-mmt=on", "-mmt4294967295", "-MMT3"];
        byte[]? first = null;
        foreach (string value in values)
        {
            string name = "m" + Array.IndexOf(values, value) + ".zip";
            (int exitCode, _, string stdErr) = _scratch.Pakko("a", value, name, "src");
            exitCode.Should().Be(0, $"{value}: {stdErr}");
            byte[] bytes = File.ReadAllBytes(_scratch.Inside(name));
            first ??= bytes;
            bytes.Should().Equal(first, $"{value} must write the bytes -mmt1 writes");
        }
    }

    [Theory]
    [InlineData("x", "o.zip", "-oout")]
    [InlineData("t", "o.zip")]
    [InlineData("l", "o.zip")]
    public void Mmt_OnAnotherCommand_IsExitSeven(params string[] args)
    {
        _scratch.Pakko("a", "o.zip", "src").ExitCode.Should().Be(0);
        string[] before = RootListing();

        (int exitCode, _, _) = _scratch.Pakko([.. args, "-mmt2"]);

        exitCode.Should().Be(7);
        RootListing().Should().Equal(before);
    }

    // --- -ssp, declined ---

    [Theory]
    [InlineData("a", "n.zip", "src")]
    [InlineData("x", "o.zip", "-oout")]
    [InlineData("t", "o.zip")]
    [InlineData("l", "o.zip")]
    [InlineData("h", "src")]
    public void Ssp_IsDeclinedOnEveryCommand_AndWritesNothing(params string[] args)
    {
        _scratch.Pakko("a", "o.zip", "src").ExitCode.Should().Be(0);
        string[] before = RootListing();

        (int exitCode, _, string stdErr) = _scratch.Pakko([.. args, "-ssp"]);

        exitCode.Should().Be(7, stdErr);
        stdErr.Should().Contain("not supported by Pakko").And.NotContain("unknown switch");
        RootListing().Should().Equal(before);
    }
}
