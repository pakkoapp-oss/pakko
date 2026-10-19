using System.Diagnostics;
using FluentAssertions;

namespace Archiver.CLI.Tests.Adversarial;

/// <summary>
/// T-F419 red team, 'x': hostile entry names, links and locks in the destination, and the
/// overwrite switches against them. The rule each test checks: no byte the user did not ask to
/// lose is lost, and exit 0 means everything asked for was done.
/// </summary>
public sealed class ExtractAdversarialTests : IDisposable
{
    private const string TarExe = @"C:\Windows\System32\tar.exe";
    private readonly AdversarialScratch _scratch = new();

    public void Dispose() => _scratch.Dispose();

    // --- links in the destination ---

    // SECURITY.md's T-F37 row says a path through a reparse point is rejected, and Core has
    // MessageCode.EntryThroughReparsePoint for it; the check walks the staging folder only, so a
    // junction the destination already holds is followed when staging is moved in. 7za 26.02
    // writes through the junction as well; this test stands on Pakko's own documented rule.
    [Theory]
    [InlineData("-aoa")]
    [InlineData("-y")]
    public void Zip_JunctionInDestination_OverwriteSwitchMustNotRewriteAFileOutsideTheDestination(string overwrite)
    {
        string archive = _scratch.Zip("arc.zip", ("link/x.txt", "FROM-ARCHIVE"));
        _scratch.Folder("out");
        string outside = _scratch.WriteFile(@"outside\x.txt", "PRECIOUS");
        _scratch.Junction(@"out\link", "outside");

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", archive, "-oout", overwrite);

        File.ReadAllText(outside).Should().Be("PRECIOUS",
            $"'x arc.zip -oout {overwrite}' must not write through out\\link into a folder outside -o (exit {exitCode}, stderr: {stdErr})");
    }

    [Fact]
    public void Zip_JunctionInDestination_NewEntryMustNotBePlantedOutsideTheDestination()
    {
        string archive = _scratch.Zip("arc.zip", ("link/planted.txt", "FROM-ARCHIVE"));
        _scratch.Folder("out");
        _scratch.Folder("outside");
        _scratch.Junction(@"out\link", "outside");

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", archive, "-oout");

        _scratch.FilesUnder("outside").Should().BeEmpty(
            $"'x arc.zip -oout' must not create a file outside -o through out\\link (exit {exitCode}, stderr: {stdErr})");
    }

    [Fact]
    public void TarGz_JunctionInDestination_OverwriteMustNotRewriteAFileOutsideTheDestination()
    {
        string stage = _scratch.Folder("stage");
        _scratch.WriteFile(@"stage\link\x.txt", "FROM-ARCHIVE");
        string archive = _scratch.Inside("arc.tar.gz");
        RunTar(stage, "-czf", archive, "link");
        _scratch.Folder("out");
        string outside = _scratch.WriteFile(@"outside\x.txt", "PRECIOUS");
        _scratch.Junction(@"out\link", "outside");

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", archive, "-oout", "-aoa");

        File.ReadAllText(outside).Should().Be("PRECIOUS",
            $"'x arc.tar.gz -oout -aoa' must not write through out\\link (exit {exitCode}, stderr: {stdErr})");
    }

    // 7za breaks the link too: the file outside keeps its bytes.
    [Fact]
    public void Zip_HardLinkInDestination_OverwriteReplacesTheLinkNotTheFileItSharesDataWith()
    {
        string archive = _scratch.Zip("arc.zip", ("a.txt", "NEW"));
        _scratch.Folder("out");
        string outside = _scratch.WriteFile(@"outside\x.txt", "PRECIOUS");
        _scratch.HardLink(@"out\a.txt", @"outside\x.txt");

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", archive, "-oout", "-aoa");

        exitCode.Should().Be(0, stdErr);
        File.ReadAllText(_scratch.Inside(@"out\a.txt")).Should().Be("NEW");
        File.ReadAllText(outside).Should().Be("PRECIOUS");
    }

    // The user's own junction as -o is a destination they chose.
    [Fact]
    public void Zip_OutputIsAJunction_ExtractsIntoItsTarget()
    {
        string archive = _scratch.Zip("arc.zip", ("x.txt", "NEW"));
        _scratch.Folder("real");
        _scratch.Junction("j", "real");

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", archive, "-oj");

        exitCode.Should().Be(0, stdErr);
        File.ReadAllText(_scratch.Inside(@"real\x.txt")).Should().Be("NEW");
    }

    // --- names Win32 folds together ---

    // 7za writes "a.txt." and "a.txt " as "a.txt_": both entries survive. Pakko folds them into
    // a.txt, so with -y (what Scoop and Chocolatey pass) one entry's data is gone and the exit is 0.
    [Theory]
    [InlineData("a.txt.")]
    [InlineData("a.txt ")]
    public void Zip_TrailingDotOrSpaceTwin_EmptyDestinationWithY_KeepsBothEntriesOrDoesNotExitZero(string twin)
    {
        string archive = _scratch.Zip("arc.zip", ("a.txt", "PLAIN"), (twin, "TWIN"));
        _scratch.Folder("out");

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", archive, "-oout", "-y");

        // Through \\?\ and without GetFullPath, which would trim a twin written under its literal name.
        string[] contents = [.. _scratch.FilesUnder("out").Select(name => File.ReadAllText(@"\\?\" + Path.Combine(_scratch.Root, "out", name)))];
        if (exitCode == 0)
        {
            contents.Should().Contain("PLAIN", $"exit 0 means every entry was extracted; stderr: {Shown(stdErr)}")
                .And.Contain("TWIN", "7za keeps the twin as a.txt_");
        }
        else
        {
            exitCode.Should().BeOneOf(1, 2);
        }
    }

    // Matches 7za: the later of two names Windows sees as one wins under -y, and -aou keeps both.
    [Fact]
    public void Zip_CaseTwin_AouKeepsBothEntries()
    {
        string archive = _scratch.Zip("arc.zip", ("a.txt", "FIRST"), ("A.TXT", "SECOND"));
        _scratch.Folder("out");

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", archive, "-oout", "-aou");

        exitCode.Should().Be(0, stdErr);
        string[] contents = [.. _scratch.FilesUnder("out").Select(name => File.ReadAllText(_scratch.Inside(Path.Combine("out", name))))];
        contents.Should().BeEquivalentTo(["FIRST", "SECOND"]);
    }

    // Pakko refuses "sub/a.txt" next to "sub\a.txt" (one name once decoded); exit 0 would need both.
    [Theory]
    [InlineData("-aou")]
    [InlineData("-y")]
    public void Zip_SeparatorTwin_NeverExitsZeroWithAnEntryMissing(string overwrite)
    {
        string archive = _scratch.Zip("arc.zip", ("sub/a.txt", "FIRST"), ("sub\\a.txt", "SECOND"));
        _scratch.Folder("out");

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", archive, "-oout", overwrite);

        string[] contents = [.. _scratch.FilesUnder("out").Select(name => File.ReadAllText(_scratch.Inside(Path.Combine("out", name))))];
        if (exitCode == 0)
            contents.Should().BeEquivalentTo(["FIRST", "SECOND"]);
        else
            exitCode.Should().Be(2, stdErr);
    }

    [Fact]
    public void Zip_CaseTwin_DefaultSkipsTheSecondAndSaysSo()
    {
        string archive = _scratch.Zip("arc.zip", ("a.txt", "FIRST"), ("A.TXT", "SECOND"));
        _scratch.Folder("out");

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", archive, "-oout");

        exitCode.Should().Be(1, "one entry was not extracted");
        stdErr.Should().Contain("A.TXT");
        File.ReadAllText(_scratch.Inside(@"out\a.txt")).Should().Be("FIRST");
    }

    // An 8.3 alias of an existing file is the same file: it must be kept. T-F420 review: an entry
    // Windows writes under another name never replaces a file already there - it is numbered
    // beside it (was: skipped, exit 1), so both are on disk.
    [Fact]
    public void Zip_ShortNameAliasOfExistingFile_DefaultKeepsTheExistingFile()
    {
        string existing = _scratch.WriteFile(@"out\longfilename.txt", "PRECIOUS");
        string? shortName = ShortNameOf(existing);
        shortName.Should().NotBeNull("8.3 names are on for %TEMP% on this machine (LONGFI~1.TXT, measured); if this volume has them off, delete this test's premise, not the check");
        string archive = _scratch.Zip("arc.zip", (shortName!, "FROM-ARCHIVE"));

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", archive, "-oout");

        File.ReadAllText(existing).Should().Be("PRECIOUS");
        exitCode.Should().Be(0, stdErr);
        File.ReadAllText(_scratch.Inside(@"out\longfilename (1).txt")).Should().Be("FROM-ARCHIVE");
    }

    // --- hostile entry names ---

    [Theory]
    [InlineData("a.txt::$DATA")]
    [InlineData("a.txt:evil")]
    public void Zip_StreamNamedEntry_EvenWithAoa_NeverTouchesTheExistingFile(string entry)
    {
        string archive = _scratch.Zip("arc.zip", (entry, "FROM-ARCHIVE"));
        string existing = _scratch.WriteFile(@"out\a.txt", "PRECIOUS");

        (int exitCode, _, _) = _scratch.Pakko("x", archive, "-oout", "-aoa");

        File.ReadAllText(existing).Should().Be("PRECIOUS");
        exitCode.Should().NotBe(0, "the only entry was refused");
    }

    [Theory]
    [InlineData(@"..\escaped.txt")]
    [InlineData("../escaped.txt")]
    [InlineData(@"out2\..\..\escaped.txt")]
    public void Zip_TraversalEntry_WithAoa_WritesNothingAboveTheDestination(string entry)
    {
        string archive = _scratch.Zip(@"a\arc.zip", (entry, "EVIL"), ("ok.txt", "ok"));
        string above = _scratch.WriteFile(@"a\escaped.txt", "PRECIOUS");

        (int exitCode, _, _) = _scratch.Pakko("x", archive, @"-oa\out", "-aoa");

        File.ReadAllText(above).Should().Be("PRECIOUS");
        exitCode.Should().Be(2);
    }

    [Theory]
    [InlineData("aux.txt")]
    [InlineData("CON")]
    [InlineData("nul.txt")]
    [InlineData("COM1.log")]
    public void Zip_ReservedDeviceNameEntry_IsNotReportedAsExtracted(string entry)
    {
        string archive = _scratch.Zip("arc.zip", (entry, "DEVICE"), ("ok.txt", "ok"));

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", archive, "-oout", "-y");

        exitCode.Should().NotBe(0, "an entry that was not written is not a clean success");
        stdErr.Should().Contain(entry);
    }

    [Fact]
    public void Zip_ControlAndBidiCharactersInName_DoNotExitZeroSilently()
    {
        string archive = _scratch.Zip("arc.zip", ("evil\u202Etxt.exe", "RTL"), ("bell\u0007.txt", "BEL"), ("ok.txt", "ok"));

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", archive, "-oout", "-y");

        File.ReadAllText(_scratch.Inside(@"out\ok.txt")).Should().Be("ok");
        if (!File.Exists(_scratch.Inside(Path.Combine("out", "bell\u0007.txt"))))
            exitCode.Should().NotBe(0, $"an entry was refused; stderr: {stdErr}");
    }

    // --- the destination fights back ---

    // A folder where the entry needs a file, and a file where it needs a folder: kept either way.
    [Fact]
    public void Zip_FolderWhereEntryFileGoes_AoaKeepsTheFolderAndItsFiles()
    {
        string archive = _scratch.Zip("arc.zip", ("a.txt", "FILE"));
        string inner = _scratch.WriteFile(@"out\a.txt\inner.txt", "PRECIOUS");

        (int exitCode, _, _) = _scratch.Pakko("x", archive, "-oout", "-aoa");

        File.ReadAllText(inner).Should().Be("PRECIOUS");
        exitCode.Should().Be(2);
    }

    // Reported as "destination file is locked by another process" today: nothing holds it; a
    // folder is in the way. The message should say what is there.
    [Fact]
    public void Zip_FolderWhereEntryFileGoes_ErrorDoesNotBlameALock()
    {
        string archive = _scratch.Zip("arc.zip", ("a.txt", "FILE"));
        _scratch.WriteFile(@"out\a.txt\inner.txt", "PRECIOUS");

        (_, _, string stdErr) = _scratch.Pakko("x", archive, "-oout", "-aoa");

        stdErr.Should().NotContain("locked by another process", "no process holds out\\a.txt; a folder of that name is in the way");
    }

    [Fact]
    public void Zip_FileWhereEntryFolderGoes_AoaKeepsTheFile()
    {
        string archive = _scratch.Zip("arc.zip", ("d/x.txt", "X"));
        string file = _scratch.WriteFile(@"out\d", "PRECIOUS");

        (int exitCode, _, _) = _scratch.Pakko("x", archive, "-oout", "-aoa");

        File.ReadAllText(file).Should().Be("PRECIOUS");
        exitCode.Should().Be(2);
    }

    [Theory]
    [InlineData("-aoa")]
    [InlineData("-y")]
    public void Zip_LockedDestinationFile_IsNotReportedAsSuccess(string overwrite)
    {
        string archive = _scratch.Zip("arc.zip", ("a.txt", "NEW"), ("b.txt", "B"));
        string locked = _scratch.WriteFile(@"out\a.txt", "OLD");

        int exitCode;
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            (exitCode, _, _) = _scratch.Pakko("x", archive, "-oout", overwrite);

        exitCode.Should().Be(2);
        File.ReadAllText(locked).Should().Be("OLD");
    }

    // 7-Zip clears the read-only attribute and overwrites; either that or a failure is fine, never
    // exit 0 with the old bytes.
    [Fact]
    public void Zip_ReadOnlyDestinationFile_Aoa_EitherOverwritesOrFails()
    {
        string archive = _scratch.Zip("arc.zip", ("a.txt", "NEW"));
        string readOnly = _scratch.WriteFile(@"out\a.txt", "OLD");
        File.SetAttributes(readOnly, FileAttributes.ReadOnly);
        try
        {
            (int exitCode, _, string stdErr) = _scratch.Pakko("x", archive, "-oout", "-aoa");

            if (exitCode == 0)
                File.ReadAllText(readOnly).Should().Be("NEW");
            else
                stdErr.Should().NotContain("locked by another process", "the file is read-only, not locked");
        }
        finally
        {
            File.SetAttributes(readOnly, FileAttributes.Normal);
        }
    }

    [Fact]
    public void Zip_EntryNamedLikeTheArchive_IntoTheArchiveFolderWithAoa_KeepsTheArchive()
    {
        string archive = _scratch.Zip("arc.zip", ("arc.zip", "INNER"), ("z.txt", "z"));
        byte[] before = File.ReadAllBytes(archive);

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", archive, "-aoa");

        if (exitCode == 0)
            File.ReadAllText(archive).Should().Be("INNER", "exit 0 with -aoa means the entry was written");
        else
            File.ReadAllBytes(archive).Should().Equal(before, stdErr);
    }

    // -o"out2 " (a trailing space): 7za makes one folder, "out2 ", and puts the files in it. Pakko
    // makes an empty "out2" as well, so the folder a user opens is the empty one.
    [Fact]
    public void Zip_OutputWithTrailingSpace_DoesNotLeaveASecondEmptyFolder()
    {
        string archive = _scratch.Zip("arc.zip", ("a.txt", "A"));

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", archive, "-oout2 ");

        exitCode.Should().Be(0, stdErr);
        string[] folders = [.. Directory.EnumerateDirectories(@"\\?\" + _scratch.Root).Select(Path.GetFileName)!];
        folders.Should().ContainSingle($"one -o folder, not two (found: {string.Join(", ", folders.Select(f => $"'{f}'"))})");
    }

    [Theory]
    [InlineData("-oo3/sub/", @"o3\sub\a.txt")]
    [InlineData(@"-oo4\..\o5", @"o5\a.txt")]
    [InlineData(@"-o.\o6\.", @"o6\a.txt")]
    public void Zip_OutputPathSpellings_LandWhereWindowsResolvesThem(string output, string expected)
    {
        string archive = _scratch.Zip("arc.zip", ("a.txt", "A"));

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", archive, output);

        exitCode.Should().Be(0, stdErr);
        File.ReadAllText(_scratch.Inside(expected)).Should().Be("A");
    }

    [Fact]
    public void Zip_LongPathOutput_ExtractsAndExitsZero()
    {
        string deep = string.Join('\\', Enumerable.Repeat(new string('d', 60), 5));
        string archive = _scratch.Zip("arc.zip", ("e/" + new string('f', 80) + ".txt", "DEEP"));

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", archive, $"-o{deep}");

        exitCode.Should().Be(0, stdErr);
        File.ReadAllText(@"\\?\" + _scratch.Inside(Path.Combine(deep, "e", new string('f', 80) + ".txt"))).Should().Be("DEEP");
    }

    // --- filters with overwrite ---

    [Fact]
    public void Zip_ExcludedEntryWithAoa_LeavesTheExistingFileAlone()
    {
        string archive = _scratch.Zip("arc.zip", ("a.txt", "NEW-A"), ("b.txt", "NEW-B"));
        string kept = _scratch.WriteFile(@"out\a.txt", "PRECIOUS");

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", archive, "-oout", "-aoa", "-x!a.txt");

        exitCode.Should().Be(0, stdErr);
        File.ReadAllText(kept).Should().Be("PRECIOUS");
        File.ReadAllText(_scratch.Inside(@"out\b.txt")).Should().Be("NEW-B");
    }

    [Fact]
    public void Zip_FilterMatchingNothing_CreatesNoOutputFolder()
    {
        string archive = _scratch.Zip("arc.zip", ("a.txt", "A"));

        (int exitCode, string stdOut, _) = _scratch.Pakko("x", archive, "-onew", "nothing.here");

        exitCode.Should().Be(0);
        stdOut.Should().Contain("No files to process");
        Directory.Exists(_scratch.Inside("new")).Should().BeFalse();
    }

    // Two archives found by one wildcard, the same name in both: the second must not silently win.
    [Fact]
    public void Zip_TwoArchivesSameEntry_DefaultKeepsTheFirstAndExitsOne()
    {
        _scratch.Zip(@"arcs\1.zip", ("a.txt", "ONE"));
        _scratch.Zip(@"arcs\2.zip", ("a.txt", "TWO"));

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", @"arcs\*.zip", "-oout");

        exitCode.Should().Be(1, stdErr);
        File.ReadAllText(_scratch.Inside(@"out\a.txt")).Should().Be("ONE");
    }

    // --- -so ---

    [Fact]
    public void Zip_SoWithTwoFiles_WritesNothingAndExitsTwo()
    {
        string archive = _scratch.Zip("arc.zip", ("a.txt", "A"), ("b.txt", "B"));

        (int exitCode, byte[] stdOut, _) = CliProcessRunnerIn(_scratch.Root, "x", archive, "-so");

        exitCode.Should().Be(2);
        stdOut.Should().BeEmpty();
        Directory.EnumerateFileSystemEntries(_scratch.Root).Should().ContainSingle("nothing lands beside the archive");
    }

    [Fact]
    public void Zip_SoWithOutput_IsACommandLineError()
    {
        string archive = _scratch.Zip("arc.zip", ("a.txt", "A"));

        (int exitCode, _, _) = _scratch.Pakko("x", archive, "-so", "-o.");

        exitCode.Should().Be(7);
    }

    private static (int ExitCode, byte[] StdOut, string StdErr) CliProcessRunnerIn(string workingDirectory, params string[] args)
    {
        var startInfo = new ProcessStartInfo(Subprocess.CliProcessRunner.ExePath)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
        };
        foreach (string arg in args)
            startInfo.ArgumentList.Add(arg);
        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("pakko did not start");
        process.StandardInput.Close();
        using var stdOut = new MemoryStream();
        Task copy = process.StandardOutput.BaseStream.CopyToAsync(stdOut);
        string stdErr = process.StandardError.ReadToEnd();
        copy.GetAwaiter().GetResult();
        process.WaitForExit();
        return (process.ExitCode, stdOut.ToArray(), stdErr);
    }

    private static string Shown(string text) => text.Length == 0 ? "(empty)" : text;

    private static void RunTar(string workingDirectory, params string[] args)
    {
        var startInfo = new ProcessStartInfo(TarExe) { WorkingDirectory = workingDirectory, UseShellExecute = false, CreateNoWindow = true };
        foreach (string arg in args)
            startInfo.ArgumentList.Add(arg);
        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("tar.exe did not start");
        process.WaitForExit();
        process.ExitCode.Should().Be(0, "tar.exe builds the fixture");
    }

    // .NET exposes no 8.3 name; the first alias Windows hands out is ~1, and only it opens the file.
    private static string? ShortNameOf(string path)
    {
        string candidate = Path.GetFileNameWithoutExtension(path).ToUpperInvariant()[..6] + "~1" + Path.GetExtension(path).ToUpperInvariant();
        return File.Exists(Path.Combine(Path.GetDirectoryName(path)!, candidate)) ? candidate : null;
    }
}
