using System.IO.Compression;
using FluentAssertions;

namespace Archiver.CLI.Tests.Adversarial;

/// <summary>
/// T-F419 red team, 'a': the archive among its own sources, names that are other files, links,
/// locks, recovery data next to the archive, and hostile switch values.
/// </summary>
public sealed class ArchiveAdversarialTests : IDisposable
{
    private readonly AdversarialScratch _scratch = new();

    public void Dispose() => _scratch.Dispose();

    private static string[] EntryNames(string zipPath)
    {
        using ZipArchive archive = ZipFile.OpenRead(zipPath);
        return [.. archive.Entries.Select(e => e.FullName)];
    }

    private static string EntryText(string zipPath, string entryName)
    {
        using ZipArchive archive = ZipFile.OpenRead(zipPath);
        using var reader = new StreamReader(archive.GetEntry(entryName)!.Open());
        return reader.ReadToEnd();
    }

    // --- recovery data next to the archive ---

    // CLI.md's -rr row writes <archive>.par2 and removes an earlier set *of that archive*. A file
    // of the user's that only has the name, not a PAR2 set at all, is replaced without -y, without
    // a warning, exit 0.
    [Fact]
    public void Rr_NewArchive_UnrelatedFileNamedLikeTheIndexPar2_IsNotOverwritten()
    {
        _scratch.WriteFile("b.txt", new string('B', 1000));
        string notes = _scratch.WriteFile("o.zip.par2", "MY NOTES, NOT A PAR2 FILE");

        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "-rr", "o.zip", "b.txt");

        File.ReadAllText(notes).Should().Be("MY NOTES, NOT A PAR2 FILE",
            $"'a -rr o.zip b.txt' (no -y) must not replace a file it did not write (exit {exitCode}, stderr: {stdErr})");
    }

    [Fact]
    public void Rr_WithY_UnrelatedFileNamedLikeTheIndexPar2_IsNotOverwritten()
    {
        _scratch.WriteFile("b.txt", new string('B', 1000));
        string notes = _scratch.WriteFile("o.zip.par2", "MY NOTES, NOT A PAR2 FILE");

        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "-y", "-rr", "o.zip", "b.txt");

        File.ReadAllText(notes).Should().Be("MY NOTES, NOT A PAR2 FILE",
            $"-y overwrites the archive named on the line, not a file that is not a set of it (exit {exitCode}, stderr: {stdErr})");
    }

    // Pinned: without -rr a non-PAR2 file beside the archive is left alone.
    [Fact]
    public void Y_OverArchive_LeavesUnrelatedPar2NamedFilesAlone()
    {
        _scratch.WriteFile("b.txt", "B");
        _scratch.Zip("o.zip", ("old.txt", "OLD"));
        string index = _scratch.WriteFile("o.zip.par2", "NOTES 1");
        string volume = _scratch.WriteFile("o.zip.vol0+1.par2", "NOTES 2");

        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "-y", "o.zip", "b.txt");

        exitCode.Should().Be(0, stdErr);
        File.ReadAllText(index).Should().Be("NOTES 1");
        File.ReadAllText(volume).Should().Be("NOTES 2");
    }

    [Theory]
    [InlineData("-rr0")]
    [InlineData("-rr101")]
    [InlineData("-rr-5")]
    [InlineData("-rr99999999999")]
    [InlineData("-rr5%")]
    [InlineData("-rrx")]
    public void Rr_HostileValue_IsExitSevenAndWritesNothing(string rr)
    {
        _scratch.WriteFile("b.txt", "B");

        (int exitCode, _, _) = _scratch.Pakko("a", rr, "o.zip", "b.txt");

        exitCode.Should().Be(7);
        File.Exists(_scratch.Inside("o.zip")).Should().BeFalse();
    }

    [Fact]
    public void Rr_WithSo_IsExitSeven()
    {
        _scratch.WriteFile("b.txt", "B");

        (int exitCode, _, _) = _scratch.Pakko("a", "-so", "-rr", "o.zip", "b.txt");

        exitCode.Should().Be(7);
    }

    // --- nothing to archive keeps what was there ---

    [Theory]
    [InlineData("missing.txt")]
    [InlineData("*.nothing")]
    public void Y_NothingToArchive_KeepsTheExistingArchive(string source)
    {
        string archive = _scratch.Zip("o.zip", ("old.txt", "OLD-ARCHIVE"));

        (int exitCode, _, _) = _scratch.Pakko("a", "-y", "o.zip", source);

        exitCode.Should().Be(2);
        EntryText(archive, "old.txt").Should().Be("OLD-ARCHIVE");
    }

    [Fact]
    public void Y_ExcludeLeavesNothing_KeepsTheExistingArchive()
    {
        _scratch.WriteFile("b.txt", "B");
        string archive = _scratch.Zip("o.zip", ("old.txt", "OLD-ARCHIVE"));

        (int exitCode, _, _) = _scratch.Pakko("a", "-y", "o.zip", "b.txt", "-x!b.txt");

        exitCode.Should().Be(2);
        EntryText(archive, "old.txt").Should().Be("OLD-ARCHIVE");
    }

    // CLI.md "Messages and naming": with nothing left, `nothing to archive`, exit 2, no archive.
    // A link as the only source is skipped and no archive is written, yet the exit is 1 - the code
    // of "done, with a warning".
    [Fact]
    public void OnlySourceIsAJunction_NoArchive_ExitsTwoNotOne()
    {
        _scratch.WriteFile(@"real\a.txt", "A");
        _scratch.Junction("j", "real");

        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "o.zip", "j");

        File.Exists(_scratch.Inside("o.zip")).Should().BeFalse();
        exitCode.Should().Be(2, $"no archive was written; stderr: {stdErr}");
    }

    [RequiresSymlinkFact]
    public void OnlySourceIsASymlink_NoArchive_ExitsTwoNotOne()
    {
        string target = _scratch.WriteFile("target.txt", "T");
        File.CreateSymbolicLink(_scratch.Inside("link.txt"), target);

        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "o.zip", "link.txt");

        File.Exists(_scratch.Inside("o.zip")).Should().BeFalse();
        exitCode.Should().Be(2, $"no archive was written; stderr: {stdErr}");
    }

    // A link inside a source is left out with a warning (exit 1), and a junction loop ends.
    [Fact]
    public void JunctionLoopInsideSource_TerminatesAndArchivesTheRest()
    {
        _scratch.WriteFile(@"src\a.txt", "A");
        _scratch.Junction(@"src\loop", "src");

        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "o.zip", "src");

        exitCode.Should().Be(1, stdErr);
        EntryNames(_scratch.Inside("o.zip")).Should().Equal("src/a.txt");
    }

    [RequiresSymlinkFact]
    public void SymlinkInsideSource_IsLeftOutWithAWarning()
    {
        _scratch.WriteFile(@"src\a.txt", "A");
        string target = _scratch.WriteFile("target.txt", "T");
        File.CreateSymbolicLink(_scratch.Inside(@"src\link.txt"), target);

        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "o.zip", "src");

        exitCode.Should().Be(1);
        stdErr.Should().Contain("link.txt");
        EntryNames(_scratch.Inside("o.zip")).Should().Equal("src/a.txt");
    }

    // --- the archive among its sources ---

    [Fact]
    public void ArchiveInsideTheSourceFolder_IsNotArchivedIntoItself()
    {
        _scratch.WriteFile(@"src\a.txt", "A");

        (int exitCode, _, string stdErr) = _scratch.Pakko("a", @"src\o.zip", "src");

        exitCode.Should().Be(0, stdErr);
        EntryNames(_scratch.Inside(@"src\o.zip")).Should().Equal("src/a.txt");
    }

    [Fact]
    public void ArchiveIsTheOnlySourceFile_WithY_ItsBytesSurviveInsideTheNewArchive()
    {
        string source = _scratch.WriteFile("a.txt", "PRECIOUS-A");

        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "-y", "a.txt", "a.txt");

        exitCode.Should().Be(0, stdErr);
        EntryText(source, "a.txt").Should().Be("PRECIOUS-A", "the source was read before its name was replaced");
    }

    [Fact]
    public void ExistingNonArchiveFileAsTheName_WithoutY_IsKept()
    {
        _scratch.WriteFile("b.txt", "B");
        string document = _scratch.WriteFile("report.docx", "PRECIOUS");

        (int exitCode, _, _) = _scratch.Pakko("a", "report.docx", "b.txt");

        exitCode.Should().Be(1);
        File.ReadAllText(document).Should().Be("PRECIOUS");
    }

    // "o.zip." is o.zip to Win32. With -y the existing o.zip is replaced (asked for), but stderr
    // then says another run took 'o.zip' and the archive went to 'o.zip.' - neither is true.
    [Theory]
    [InlineData("o.zip.")]
    [InlineData("o.zip ")]
    public void TrailingDotOrSpaceName_WithY_DoesNotClaimTheNameWasTakenByAnotherRun(string name)
    {
        _scratch.WriteFile("b.txt", "B");
        _scratch.Zip("o.zip", ("old.txt", "OLD"));

        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "-y", name, "b.txt");

        string[] names = EntryNames(_scratch.Inside("o.zip"));
        names.Should().Equal("b.txt");
        stdErr.Should().NotContain("was taken while compressing", "no other run took o.zip: this run replaced it, as -y asked");
        exitCode.Should().Be(0, stdErr);
    }

    [Fact]
    public void TrailingDotName_WithoutY_KeepsTheExistingArchive()
    {
        _scratch.WriteFile("b.txt", "B");
        string archive = _scratch.Zip("o.zip", ("old.txt", "OLD"));

        (int exitCode, _, _) = _scratch.Pakko("a", "o.zip.", "b.txt");

        exitCode.Should().Be(1);
        EntryText(archive, "old.txt").Should().Be("OLD");
    }

    // A device name as the archive. NUL: Pakko's last-resort handler caught an ArgumentNullException
    // (exit 2, a stack trace). CON: exit 0 and a real file named "CON", which Pakko's own 'x'
    // refuses to create (T-F39) and Explorer cannot delete. 7za (without -sae) writes CON.zip.
    [Theory]
    [InlineData("NUL")]
    [InlineData("CON")]
    public void DeviceNameAsArchive_NeitherCrashesNorCreatesAReservedNameFile(string device)
    {
        _scratch.WriteFile("b.txt", "B");

        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "-sae", device, "b.txt");

        stdErr.Should().NotContain("Unexpected error").And.NotContain("Exception");
        Directory.EnumerateFileSystemEntries(@"\\?\" + _scratch.Root).Select(Path.GetFileName)
            .Should().NotContain(device, $"a file named {device} is one Windows tools cannot open (exit {exitCode})");
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("b.txt:stream")]
    public void DeviceOrStreamAsTheOnlySource_IsNothingToArchive(string source)
    {
        _scratch.WriteFile("b.txt", "B");

        (int exitCode, _, _) = _scratch.Pakko("a", "o.zip", source);

        exitCode.Should().Be(2);
        File.Exists(_scratch.Inside("o.zip")).Should().BeFalse();
    }

    // 7za reads a locked source as a warning (exit 1) and archives the rest; CLI.md's table has the
    // same 1 for a missing source. Pakko writes the archive and exits 2, the code for "failed".
    [Fact]
    public void LockedSource_RestArchived_ExitsOneLikeAMissingSource()
    {
        _scratch.WriteFile(@"src\a.txt", "A");
        _scratch.WriteFile(@"src\b.txt", "B");

        int exitCode;
        string stdErr;
        using (new FileStream(_scratch.Inside(@"src\a.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
            (exitCode, _, stdErr) = _scratch.Pakko("a", "o.zip", "src");

        string archive = _scratch.Inside("o.zip");
        File.Exists(archive).Should().BeTrue();
        EntryNames(archive).Should().Equal("src/b.txt");
        exitCode.Should().Be(1, $"the archive was written with the readable files, as 7za does (exit 1); stderr: {stdErr}");
    }

    [Fact]
    public void ArchiveUnderAFileName_IsAnErrorAndTheFileIsKept()
    {
        string file = _scratch.WriteFile("b.txt", "B");

        (int exitCode, _, _) = _scratch.Pakko("a", @"b.txt\o.zip", "b.txt");

        exitCode.Should().Be(2);
        File.ReadAllText(file).Should().Be("B");
    }

    [Fact]
    public void ShortNameSource_IsArchived()
    {
        _scratch.WriteFile("longfilename.txt", "L");

        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "o.zip", "LONGFI~1.TXT");

        exitCode.Should().Be(0, stdErr);
        EntryNames(_scratch.Inside("o.zip")).Should().ContainSingle();
    }

    [Fact]
    public void LongPathSource_IsArchivedWhole()
    {
        string deep = @"src\" + string.Join('\\', Enumerable.Repeat(new string('d', 50), 6));
        Directory.CreateDirectory(@"\\?\" + _scratch.Inside(deep));
        File.WriteAllText(@"\\?\" + _scratch.Inside(Path.Combine(deep, "f.txt")), "DEEP");

        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "o.zip", "src");

        exitCode.Should().Be(0, stdErr);
        EntryText(_scratch.Inside("o.zip"), deep.Replace('\\', '/') + "/f.txt").Should().Be("DEEP");
    }

    // --- hostile values ---

    [Theory]
    [InlineData("-p\u0436")]
    [InlineData("-pa\tb")]
    [InlineData("-pa\u202Eb")]
    public void NonPrintablePassword_OnA_IsExitSevenAndNothingIsWritten(string password)
    {
        _scratch.WriteFile("b.txt", "B");

        (int exitCode, _, string stdErr) = _scratch.Pakko("a", password, "p.zip", "b.txt");

        exitCode.Should().Be(7);
        stdErr.Should().NotContain(password[2..]);
        File.Exists(_scratch.Inside("p.zip")).Should().BeFalse();
    }

    [Fact]
    public void HundredCharacterPassword_OnA_IsExitSeven()
    {
        _scratch.WriteFile("b.txt", "B");

        (int exitCode, _, _) = _scratch.Pakko("a", "-p" + new string('x', 100), "p.zip", "b.txt");

        exitCode.Should().Be(7);
        File.Exists(_scratch.Inside("p.zip")).Should().BeFalse();
    }

    [Theory]
    [InlineData("-mx=-1")]
    [InlineData("-mx=abc")]
    [InlineData("-mx=1.5")]
    public void BadCompressionLevel_IsExitSevenAndWritesNothing(string level)
    {
        _scratch.WriteFile("b.txt", "B");

        (int exitCode, _, _) = _scratch.Pakko("a", level, "m.zip", "b.txt");

        exitCode.Should().Be(7);
        File.Exists(_scratch.Inside("m.zip")).Should().BeFalse();
    }

    [Fact]
    public void PasswordWithTar_IsExitSevenAndWritesNothing()
    {
        _scratch.WriteFile("b.txt", "B");

        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "-ttar", "-psecret", "t.tar", "b.txt");

        exitCode.Should().Be(7);
        stdErr.Should().NotContain("secret");
        File.Exists(_scratch.Inside("t.tar")).Should().BeFalse();
    }
}
