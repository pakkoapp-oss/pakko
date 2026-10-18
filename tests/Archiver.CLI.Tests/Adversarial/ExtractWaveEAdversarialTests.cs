using Archiver.CLI.Tests.Subprocess;
using FluentAssertions;

namespace Archiver.CLI.Tests.Adversarial;

/// <summary>
/// T-F419 red team, wave E's extraction switches: -aot (rename the file already there) and -spe
/// (drop the root folder named like -o). Every version of a file that was on disk or in the
/// archive must be somewhere afterwards, and exit 0 only when that is so.
/// </summary>
public sealed class ExtractWaveEAdversarialTests : IDisposable
{
    private readonly AdversarialScratch _scratch = new();

    public ExtractWaveEAdversarialTests()
    {
        _scratch.Zip("arc.zip", ("a.txt", "NEW"), ("sub/s.txt", "SNEW"));
    }

    public void Dispose() => _scratch.Dispose();

    private string[] Tree(string relative)
    {
        string root = _scratch.Inside(relative);
        if (!Directory.Exists(root))
            return [];
        return [.. Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path) + (File.Exists(path) ? "=" + File.ReadAllText(path) : @"\"))
            .Order(StringComparer.Ordinal)];
    }

    // --- -aot ---

    [Fact]
    public void Aot_ExistingFile_IsRenamedAndTheExtractedOneTakesItsName()
    {
        _scratch.WriteFile(@"out\a.txt", "OLD");

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", "arc.zip", "-oout", "-aot");

        exitCode.Should().Be(0, stdErr);
        Tree("out").Should().Equal("a (1).txt=OLD", "a.txt=NEW", @"sub\", @"sub\s.txt=SNEW");
    }

    [Fact]
    public void Aot_ThreeRuns_KeepEveryVersion()
    {
        _scratch.WriteFile(@"out\a.txt", "OLD");

        for (int run = 0; run < 3; run++)
        {
            (int exitCode, _, string stdErr) = _scratch.Pakko("x", "arc.zip", "-oout", "-aot");
            exitCode.Should().Be(0, stdErr);
        }

        string[] versions = [.. Directory.GetFiles(_scratch.Inside("out"), "a*.txt").Select(File.ReadAllText).Order(StringComparer.Ordinal)];
        versions.Should().Equal("NEW", "NEW", "NEW", "OLD");
        File.ReadAllText(_scratch.Inside(@"out\a.txt")).Should().Be("NEW");
    }

    // CLI.md: the new name is "never ... one a folder already has".
    [Fact]
    public void Aot_NumberedNameTakenByAFolder_FolderIsUntouched()
    {
        _scratch.WriteFile(@"out\a.txt", "OLD");
        _scratch.WriteFile(@"out\a (1).txt\inner.txt", "INNER");

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", "arc.zip", "-oout", "-aot");

        exitCode.Should().Be(0, stdErr);
        File.ReadAllText(_scratch.Inside(@"out\a (1).txt\inner.txt")).Should().Be("INNER");
        File.ReadAllText(_scratch.Inside(@"out\a (2).txt")).Should().Be("OLD");
        File.ReadAllText(_scratch.Inside(@"out\a.txt")).Should().Be("NEW");
    }

    // CLI.md: never "a name the archive is still to write".
    [Fact]
    public void Aot_NumberedNameIsAnEntryOfTheArchive_NothingIsOverwritten()
    {
        _scratch.Zip("two.zip", ("a.txt", "NEW"), ("a (1).txt", "NEW1"));
        _scratch.WriteFile(@"out\a.txt", "OLD");
        _scratch.WriteFile(@"out\a (1).txt", "OLD1");

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", "two.zip", "-oout", "-aot");

        exitCode.Should().Be(0, stdErr);
        Tree("out").Select(e => e.Split('=')[1]).Order(StringComparer.Ordinal).Should().Equal("NEW", "NEW1", "OLD", "OLD1");
        File.ReadAllText(_scratch.Inside(@"out\a.txt")).Should().Be("NEW");
        File.ReadAllText(_scratch.Inside(@"out\a (1).txt")).Should().Be("NEW1");
    }

    [Fact]
    public void Aot_CaseOnlyCollision_OldFileIsRenamed()
    {
        _scratch.WriteFile(@"out\A.TXT", "OLD");

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", "arc.zip", "-oout", "-aot");

        exitCode.Should().Be(0, stdErr);
        Tree("out").Where(e => !e.StartsWith("sub", StringComparison.Ordinal)).Select(e => e.Split('=')[1]).Order(StringComparer.Ordinal)
            .Should().Equal("NEW", "OLD");
    }

    // Two entries of one name: CLI.md, "the later one is renamed itself".
    [Theory]
    [InlineData("a.txt", "a.txt")]
    [InlineData("a.txt", "A.TXT")]
    public void Aot_TwoEntriesOfOneName_AndAFileOnDisk_AllThreeKept(string first, string second)
    {
        _scratch.Zip("dup.zip", (first, "FIRST"), (second, "SECOND"));
        _scratch.WriteFile(@"out\a.txt", "OLD");

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", "dup.zip", "-oout", "-aot");

        exitCode.Should().Be(0, stdErr);
        Tree("out").Select(e => e.Split('=')[1]).Order(StringComparer.Ordinal).Should().Equal("FIRST", "OLD", "SECOND");
    }

    // CLI.md's row says what happens to a file; it is silent on a folder of the entry's name.
    // 7za 26.02 -aot renames the folder (a_1.txt) and writes the file, exit 0. Pakko fails the
    // entry "destination file is locked by another process", which nothing is.
    [Fact]
    public void Aot_FolderWhereTheEntryGoes_IsRenamedLike7Zip_NotReportedLocked()
    {
        _scratch.WriteFile(@"out\a.txt\inner.txt", "INNER");

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", "arc.zip", "-oout", "-aot");

        string[] inner = Directory.GetFiles(_scratch.Inside("out"), "inner.txt", SearchOption.AllDirectories);
        inner.Should().ContainSingle("the folder's content must survive");
        File.ReadAllText(inner[0]).Should().Be("INNER");
        stdErr.Should().NotContain("locked by another process", "no process holds anything; the name is a folder");
        exitCode.Should().Be(0, $"7za -aot renames the folder and extracts the file; stderr: {stdErr}");
        File.ReadAllText(_scratch.Inside(@"out\a.txt")).Should().Be("NEW");
    }

    [Fact]
    public void Aot_EntryIsAFolderWhereDiskHasAFile_FileIsKept()
    {
        _scratch.Zip("dir.zip", ("a.txt/inner.txt", "DEEP"));
        _scratch.WriteFile(@"out\a.txt", "OLD");

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", "dir.zip", "-oout", "-aot");

        string[] versions = [.. Directory.GetFiles(_scratch.Inside("out"), "*", SearchOption.AllDirectories).Select(File.ReadAllText)];
        versions.Should().Contain("OLD", $"exit {exitCode}: {stdErr}");
        if (exitCode == 0)
            versions.Should().Contain("DEEP");
    }

    // CLI.md: "a file that cannot be moved in leaves the existing one under its own name (... exit 2)".
    [Fact]
    public void Aot_ExistingFileLocked_KeepsItsNameAndBytes_ExitTwo()
    {
        string existing = _scratch.WriteFile(@"out\a.txt", "OLD");
        int exitCode;
        string stdErr;
        using (new FileStream(existing, FileMode.Open, FileAccess.Read, FileShare.None))
            (exitCode, _, stdErr) = _scratch.Pakko("x", "arc.zip", "-oout", "-aot");

        exitCode.Should().Be(2, stdErr);
        File.ReadAllText(existing).Should().Be("OLD");
        File.ReadAllText(_scratch.Inside(@"out\sub\s.txt")).Should().Be("SNEW");
    }

    [Fact]
    public void Aot_ReadOnlyExisting_IsRenamedKeepingItsAttribute()
    {
        string existing = _scratch.WriteFile(@"out\a.txt", "OLD");
        File.SetAttributes(existing, FileAttributes.ReadOnly);

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", "arc.zip", "-oout", "-aot");

        exitCode.Should().Be(0, stdErr);
        string renamed = _scratch.Inside(@"out\a (1).txt");
        File.ReadAllText(renamed).Should().Be("OLD");
        File.GetAttributes(renamed).Should().HaveFlag(FileAttributes.ReadOnly);
        File.SetAttributes(renamed, FileAttributes.Normal);
    }

    [Fact]
    public void Aot_HardLinkedExisting_OtherNameKeepsItsBytes()
    {
        _scratch.WriteFile("outside.txt", "OUTSIDE");
        _scratch.Folder("out");
        _scratch.HardLink(@"out\a.txt", "outside.txt");

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", "arc.zip", "-oout", "-aot");

        exitCode.Should().Be(0, stdErr);
        File.ReadAllText(_scratch.Inside("outside.txt")).Should().Be("OUTSIDE", "the new file is a new file, never written through the old one's link");
        File.ReadAllText(_scratch.Inside(@"out\a.txt")).Should().Be("NEW");
    }

    [RequiresSymlinkFact]
    public void Aot_SymlinkedExisting_TargetKeepsItsBytes()
    {
        string target = _scratch.WriteFile("target.txt", "TARGET");
        _scratch.Folder("out");
        File.CreateSymbolicLink(_scratch.Inside(@"out\a.txt"), target);

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", "arc.zip", "-oout", "-aot");

        exitCode.Should().Be(0, stdErr);
        File.ReadAllText(target).Should().Be("TARGET");
        File.ReadAllText(_scratch.Inside(@"out\a.txt")).Should().Be("NEW");
    }

    // -y answers prompts; it does not turn -aot into an overwrite (7za: the same).
    [Fact]
    public void Aot_WithY_StillRenames()
    {
        _scratch.WriteFile(@"out\a.txt", "OLD");

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", "arc.zip", "-oout", "-aot", "-y");

        exitCode.Should().Be(0, stdErr);
        File.ReadAllText(_scratch.Inside(@"out\a (1).txt")).Should().Be("OLD");
    }

    [Theory]
    [InlineData("-aoa")]
    [InlineData("-aou")]
    [InlineData("-aos")]
    [InlineData("-aot")]
    [InlineData("-AOT")]
    public void Aot_WithAnotherOverwriteMode_IsExitSevenAndChangesNothing(string other)
    {
        _scratch.WriteFile(@"out\a.txt", "OLD");

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", "arc.zip", "-oout", "-aot", other);

        exitCode.Should().Be(7, stdErr);
        Tree("out").Should().Equal("a.txt=OLD");
    }

    [Theory]
    [InlineData(@"-o.\out\")]
    [InlineData("-oout/")]
    [InlineData("-oOUT")]
    public void Aot_OutputSpelledAnotherWay_RenamesTheSameFile(string output)
    {
        _scratch.WriteFile(@"out\a.txt", "OLD");

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", "arc.zip", output, "-aot");

        exitCode.Should().Be(0, stdErr);
        File.ReadAllText(_scratch.Inside(@"out\a (1).txt")).Should().Be("OLD");
        File.ReadAllText(_scratch.Inside(@"out\a.txt")).Should().Be("NEW");
    }

    [RequiresTarExe]
    public void Aot_TarGz_RenamesLikeZip()
    {
        _scratch.WriteFile(@"pack\a.txt", "NEW");
        (int packCode, _, string packErr) = _scratch.PakkoIn("pack", "a", "-ttar.gz", @"..\arc.tar.gz", "a.txt");
        packCode.Should().Be(0, packErr);
        _scratch.WriteFile(@"out\a.txt", "OLD");

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", "arc.tar.gz", "-oout", "-aot");

        exitCode.Should().Be(0, stdErr);
        Tree("out").Should().Equal("a (1).txt=OLD", "a.txt=NEW");
    }

    [Theory]
    [InlineData("a", "o.zip", "src")]
    [InlineData("t", "arc.zip")]
    [InlineData("l", "arc.zip")]
    public void Aot_OnACommandThatWritesNoFiles_IsExitSeven(params string[] args)
    {
        _scratch.WriteFile(@"src\b.txt", "B");

        (int exitCode, _, _) = _scratch.Pakko([.. args, "-aot"]);

        exitCode.Should().Be(7);
        File.Exists(_scratch.Inside("o.zip")).Should().BeFalse();
    }

    // --- -spe ---

    private void SrcZip() => _scratch.Zip("src.zip", ("src/a.txt", "A"), ("src/sub/s.txt", "S"));

    [Theory]
    [InlineData(@"-oout\src")]
    [InlineData(@"-oout\SRC")]
    [InlineData(@"-oout\src\")]
    [InlineData("-oout/src")]
    public void Spe_RootNamedLikeOutput_IsDropped(string output)
    {
        SrcZip();

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", "src.zip", output, "-spe");

        exitCode.Should().Be(0, stdErr);
        Tree(@"out\src").Should().Equal("a.txt=A", @"sub\", @"sub\s.txt=S");
    }

    // CLI.md: "-o. names ., so nothing is dropped", even in a folder named like the root.
    [Fact]
    public void Spe_OutputDot_InAFolderNamedLikeTheRoot_DropsNothing()
    {
        SrcZip();
        _scratch.Folder("src");

        (int exitCode, _, string stdErr) = _scratch.PakkoIn("src", "x", @"..\src.zip", "-o.", "-spe");

        exitCode.Should().Be(0, stdErr);
        Tree("src").Should().Equal(@"src\", @"src\a.txt=A", @"src\sub\", @"src\sub\s.txt=S");
    }

    [Fact]
    public void Spe_NestedRootOfTheSameName_IsDroppedOnce()
    {
        _scratch.Zip("nest.zip", ("src/src/d.txt", "D"));

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", "nest.zip", @"-oout\src", "-spe");

        exitCode.Should().Be(0, stdErr);
        Tree(@"out\src").Should().Equal(@"src\", @"src\d.txt=D");
    }

    // CLI.md: "a file of that name, or any entry beside the folder, keeps it".
    [Fact]
    public void Spe_SingleFileArchiveNamedLikeOutput_KeepsTheFile()
    {
        _scratch.Zip("one.zip", ("a.txt", "A"));

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", "one.zip", @"-oout\a.txt", "-spe");

        exitCode.Should().Be(0, stdErr);
        Tree(@"out\a.txt").Should().Equal("a.txt=A");
    }

    [Fact]
    public void Spe_EntryBesideTheRoot_KeepsTheRoot()
    {
        _scratch.Zip("beside.zip", ("src/a.txt", "A"), ("srcx.txt", "X"));

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", "beside.zip", @"-oout\src", "-spe");

        exitCode.Should().Be(0, stdErr);
        Tree(@"out\src").Should().Equal(@"src\", @"src\a.txt=A", "srcx.txt=X");
    }

    // Dropping the root must not turn "src/../../x" into a path that escapes -o.
    [Theory]
    [InlineData("src/../../escape.txt")]
    [InlineData("src/../escape.txt")]
    [InlineData("src/..")]
    [InlineData("src/../../../escape.txt")]
    public void Spe_TraversalUnderTheDroppedRoot_StaysRefused(string hostile)
    {
        _scratch.Zip("trav.zip", ("src/a.txt", "A"), (hostile, "EVIL"));

        (int exitCode, _, _) = _scratch.Pakko("x", "trav.zip", @"-oout\src", "-spe");

        exitCode.Should().Be(2);
        Directory.GetFiles(_scratch.Root, "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith("trav.zip", StringComparison.Ordinal) && File.ReadAllText(f) == "EVIL")
            .Should().BeEmpty();
    }

    [Fact]
    public void Spe_ExistingFile_DefaultSkip_IsKept()
    {
        SrcZip();
        _scratch.WriteFile(@"out\src\a.txt", "PRECIOUS");

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", "src.zip", @"-oout\src", "-spe", "-aos");

        exitCode.Should().BeLessThan(2, stdErr);
        File.ReadAllText(_scratch.Inside(@"out\src\a.txt")).Should().Be("PRECIOUS");
    }

    [Fact]
    public void Spe_WithAot_RenamesTheFileAtTheDroppedPath()
    {
        SrcZip();
        _scratch.WriteFile(@"out\src\a.txt", "OLD");

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", "src.zip", @"-oout\src", "-spe", "-aot");

        exitCode.Should().Be(0, stdErr);
        File.ReadAllText(_scratch.Inside(@"out\src\a.txt")).Should().Be("A");
        File.ReadAllText(_scratch.Inside(@"out\src\a (1).txt")).Should().Be("OLD");
    }

    [Fact]
    public void SpeMinus_KeepsTheRoot()
    {
        SrcZip();

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", "src.zip", @"-oout\src", "-spe-");

        exitCode.Should().Be(0, stdErr);
        Tree(@"out\src").Should().Equal(@"src\", @"src\a.txt=A", @"src\sub\", @"src\sub\s.txt=S");
    }

    [RequiresTarExe]
    public void Spe_TarGz_RootIsDropped()
    {
        _scratch.WriteFile(@"pack\src\a.txt", "A");
        (int packCode, _, string packErr) = _scratch.PakkoIn("pack", "a", "-ttar.gz", @"..\src.tar.gz", "src");
        packCode.Should().Be(0, packErr);

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", "src.tar.gz", @"-oout\src", "-spe");

        exitCode.Should().Be(0, stdErr);
        Tree(@"out\src").Should().Equal("a.txt=A");
    }

    [Theory]
    [InlineData("a", "o.zip", "src")]
    [InlineData("t", "arc.zip")]
    public void Spe_OnAnotherCommand_IsExitSevenNamingTheSwitch(params string[] args)
    {
        _scratch.WriteFile(@"src\b.txt", "B");

        (int exitCode, _, string stdErr) = _scratch.Pakko([.. args, "-spe"]);

        exitCode.Should().Be(7);
        stdErr.Should().Contain("-spe");
        File.Exists(_scratch.Inside("o.zip")).Should().BeFalse();
    }
}
