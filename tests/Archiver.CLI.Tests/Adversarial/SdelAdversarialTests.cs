using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Archiver.CLI.Tests.Subprocess;
using FluentAssertions;

namespace Archiver.CLI.Tests.Adversarial;

/// <summary>
/// T-F419 red team, wave E's -sdel: CLI.md's row is the spec. Each test checks that no byte the
/// user did not ask to lose goes, that what goes is in the archive, and that a refusal CLI.md
/// promises ("before anything is written") holds for every spelling of the same folder.
/// </summary>
public sealed class SdelAdversarialTests : IDisposable
{
    private readonly AdversarialScratch _scratch = new();

    public SdelAdversarialTests()
    {
        _scratch.WriteFile(@"src\a.txt", "A");
        _scratch.WriteFile(@"src\b.txt", "B");
        _scratch.WriteFile(@"src\c.dat", "C");
        _scratch.WriteFile(@"src\sub\s.txt", "S");
    }

    public void Dispose() => _scratch.Dispose();

    /// <summary>Every entry under a folder of the scratch as "relative=content" or "relative\"; links are not entered.</summary>
    private string[] Tree(string relative)
    {
        string root = _scratch.Inside(relative);
        if (!Directory.Exists(root))
            return [];
        return [.. Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path) + (File.Exists(path) ? "=" + File.ReadAllText(path) : @"\"))
            .Order(StringComparer.Ordinal)];
    }

    private static readonly string[] FullSource = ["a.txt=A", "b.txt=B", "c.dat=C", @"sub\", @"sub\s.txt=S"];

    /// <summary>The archive's files by name (last path part) and content.</summary>
    private static string[] ZipFiles(string zipPath)
    {
        using ZipArchive archive = ZipFile.OpenRead(zipPath);
        return [.. archive.Entries.Where(e => !e.FullName.EndsWith('/'))
            .Select(e =>
            {
                using var reader = new StreamReader(e.Open());
                return e.FullName.Split('/')[^1] + "=" + reader.ReadToEnd();
            })
            .Order(StringComparer.Ordinal)];
    }

    // --- the baseline the rest are measured against ---

    [Fact]
    public void Sdel_PlainFolder_ArchivesEverythingThenDeletesIt()
    {
        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "-sdel", "o.zip", "src");

        exitCode.Should().Be(0, stdErr);
        ZipFiles(_scratch.Inside("o.zip")).Should().Equal("a.txt=A", "b.txt=B", "c.dat=C", "s.txt=S");
        Directory.Exists(_scratch.Inside("src")).Should().BeFalse("CLI.md: the folders go too, deepest first, once empty");
    }

    // --- the current-folder refusal, spelled other ways ---

    // CLI.md: "Refused with exit 7 before anything is written: ... a source that is or holds the
    // current folder". The check compares strings; the same folder through the \\?\ prefix is not
    // the same string.
    [Fact]
    public void Sdel_FolderHoldingTheCurrentFolder_SpelledWithLongPathPrefix_IsRefused()
    {
        _scratch.WriteFile(@"src\work\w.txt", "W");
        string[] before = Tree("src");

        (int exitCode, _, string stdErr) = _scratch.PakkoIn(@"src\work", "a", "-sdel", @"..\..\o.zip", @"\\?\" + _scratch.Inside("src"));

        Tree("src").Should().Equal(before, $"src holds the current folder, so -sdel is refused before anything is written (exit {exitCode}, stderr: {stdErr})");
        exitCode.Should().Be(7, stdErr);
        File.Exists(_scratch.Inside("o.zip")).Should().BeFalse("refused before anything is written");
    }

    // Same rule, the current folder reached through a junction and the source named by its real path.
    [Fact]
    public void Sdel_CurrentFolderEnteredThroughJunction_RealPathOfItsParent_IsRefused()
    {
        _scratch.WriteFile(@"src\work\w.txt", "W");
        _scratch.Junction("jsrc", "src");
        string[] before = Tree("src");

        (int exitCode, _, string stdErr) = _scratch.PakkoIn(@"jsrc\work", "a", "-sdel", @"..\..\o.zip", _scratch.Inside("src"));

        Tree("src").Should().Equal(before, $"the current folder is src\\work whichever path reached it (exit {exitCode}, stderr: {stdErr})");
        exitCode.Should().Be(7, stdErr);
    }

    // Same rule through the folder's 8.3 short name.
    [RequiresShortNamesFact]
    public void Sdel_FolderHoldingTheCurrentFolder_SpelledAsShortName_IsRefused()
    {
        _scratch.WriteFile(@"longsourcefoldername\work\w.txt", "W");
        _scratch.WriteFile(@"longsourcefoldername\a.txt", "A");
        string longPath = _scratch.Inside("longsourcefoldername");
        string shortPath = ShortNames.Of(longPath);
        shortPath.Should().NotBe(longPath, "the attribute checked that this volume makes short names");
        string[] before = Tree("longsourcefoldername");

        (int exitCode, _, string stdErr) = _scratch.PakkoIn(@"longsourcefoldername\work", "a", "-sdel", @"..\..\o.zip", shortPath);

        Tree("longsourcefoldername").Should().Equal(before, $"{shortPath} is the folder holding the current one (exit {exitCode}, stderr: {stdErr})");
        exitCode.Should().Be(7, stdErr);
    }

    [Theory]
    [InlineData("..")]
    [InlineData(@"..\")]
    [InlineData("../")]
    [InlineData(@"..\..")]
    [InlineData(@"..\..\..\src.")]
    [InlineData(@"..\..\..\SRC")]
    public void Sdel_CurrentFolderOrItsParent_PlainSpellings_AreRefused(string source)
    {
        _scratch.WriteFile(@"src\sub\work\w.txt", "W");
        string[] before = Tree("src");

        (int exitCode, _, string stdErr) = _scratch.PakkoIn(@"src\sub\work", "a", "-sdel", @"..\..\..\o.zip", source);

        exitCode.Should().Be(7, stdErr);
        Tree("src").Should().Equal(before);
        File.Exists(_scratch.Inside("o.zip")).Should().BeFalse();
    }

    // CLI.md: "`.` names the current folder's items, which go, while the folder stays".
    [Fact]
    public void Sdel_Dot_WithYOverAnArchiveInTheSameFolder_KeepsTheNewArchiveAndTheFolder()
    {
        _scratch.WriteFile(@"src\o.zip", "OLD ARCHIVE BYTES");

        (int exitCode, _, string stdErr) = _scratch.PakkoIn("src", "a", "-sdel", "-y", "o.zip", ".");

        string archive = _scratch.Inside(@"src\o.zip");
        File.Exists(archive).Should().BeTrue($"the archive just written must never be deleted by its own -sdel (exit {exitCode}, stderr: {stdErr})");
        ZipFiles(archive).Should().Contain(["a.txt=A", "b.txt=B", "c.dat=C", "s.txt=S"]);
        Directory.Exists(_scratch.Inside("src")).Should().BeTrue("the current folder stays");
        exitCode.Should().BeLessThan(2, stdErr);
    }

    // --- -so: the archive goes to a reader that may stop ---

    // The archive's only copy is the stream. A reader that stops after a few bytes has not got
    // the archive; deleting the sources then loses them, and exit 0 says all is well.
    [Fact]
    public void Sdel_ToStdout_ReaderStopsEarly_DeletesNothingAndFails()
    {
        byte[] noise = RandomNumberGenerator.GetBytes(3 * 1024 * 1024);
        File.WriteAllBytes(_scratch.Inside(@"src\big.bin"), noise);
        string[] before = Tree("src").Where(e => !e.StartsWith("big.bin", StringComparison.Ordinal)).ToArray();

        (int exitCode, int bytesRead, string stdErr) = PakkoStdoutReadingOnly(16, "a", "-sdel", "-so", "o.zip", "src");

        bytesRead.Should().Be(16, "the harness read the start of the stream");
        File.Exists(_scratch.Inside(@"src\big.bin")).Should().BeTrue(
            $"only 16 bytes of the archive reached anyone, so -sdel must keep every source (exit {exitCode}, stderr: {stdErr})");
        File.ReadAllBytes(_scratch.Inside(@"src\big.bin")).Should().Equal(noise);
        Tree("src").Where(e => !e.StartsWith("big.bin", StringComparison.Ordinal)).Should().Equal(before);
        exitCode.Should().Be(2, "the archive was not delivered");
    }

    // The same stopped reader without -sdel: 7za 26.02 says "System ERROR: The pipe is being
    // closed", exit 2. Exit 0 tells a script the archive arrived.
    [Fact]
    public void ToStdout_ReaderStopsEarly_IsNotExitZero()
    {
        File.WriteAllBytes(_scratch.Inside(@"src\big.bin"), RandomNumberGenerator.GetBytes(3 * 1024 * 1024));

        (int exitCode, int bytesRead, string stdErr) = PakkoStdoutReadingOnly(16, "a", "-so", "o.zip", "src");

        bytesRead.Should().Be(16);
        exitCode.Should().Be(2, $"the archive did not reach the reader, as 7za reports (stderr: {stdErr})");
    }

    [Fact]
    public void Sdel_ToStdout_ReadInFull_StreamHoldsEverythingDeleted()
    {
        (int exitCode, byte[] stdOut, string stdErr) = PakkoStdoutReadAll("a", "-sdel", "-so", "o.zip", "src");

        exitCode.Should().Be(0, stdErr);
        string streamed = _scratch.Inside("streamed.zip");
        File.WriteAllBytes(streamed, stdOut);
        ZipFiles(streamed).Should().Equal("a.txt=A", "b.txt=B", "c.dat=C", "s.txt=S");
        Directory.Exists(_scratch.Inside("src")).Should().BeFalse();
    }

    // --- links ---

    [Fact]
    public void Sdel_JunctionInsideTheSource_NothingBehindItIsTouched()
    {
        _scratch.WriteFile(@"target\t.txt", "T");
        _scratch.Junction(@"src\j", "target");

        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "-sdel", "o.zip", "src");

        Tree("target").Should().Equal("t.txt=T");
        Directory.Exists(_scratch.Inside(@"src\j")).Should().BeTrue("CLI.md: a link is never deleted");
        exitCode.Should().Be(1, stdErr);
    }

    [Fact]
    public void Sdel_JunctionAsTheSource_IsNeitherEnteredNorDeleted()
    {
        _scratch.WriteFile(@"target\t.txt", "T");
        _scratch.Junction("j", "target");

        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "-sdel", "o.zip", @"j\");

        Tree("target").Should().Equal("t.txt=T");
        Directory.Exists(_scratch.Inside("j")).Should().BeTrue();
        // A link alone is nothing to archive (T-F419 E): exit 2, as with no source left.
        exitCode.Should().Be(2, stdErr);
    }

    // CLI.md: "Unlike 7-Zip a link is never entered or deleted (7-Zip follows a junction and deletes
    // the files behind it)". A source path that runs through a junction enters it.
    [Theory]
    [InlineData(@"j\*.txt")]
    [InlineData(@"j\sub")]
    [InlineData(@"j\t.txt")]
    public void Sdel_SourcePathThroughAJunction_DeletesNothingBehindIt(string source)
    {
        _scratch.WriteFile(@"target\t.txt", "T");
        _scratch.WriteFile(@"target\sub\u.txt", "U");
        _scratch.Junction("j", "target");

        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "-sdel", "o.zip", source);

        Tree("target").Should().Equal([@"sub\", @"sub\u.txt=U", "t.txt=T"],
            $"'{source}' reaches the files through the junction j; CLI.md says -sdel never enters a link (exit {exitCode}, stderr: {stdErr})");
    }

    [Fact]
    public void Sdel_HardLinkInsideTheSource_TheOtherNameKeepsItsBytes()
    {
        _scratch.WriteFile("outside.txt", "HL");
        _scratch.HardLink(@"src\h.txt", "outside.txt");

        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "-sdel", "o.zip", "src");

        exitCode.Should().Be(0, stdErr);
        File.ReadAllText(_scratch.Inside("outside.txt")).Should().Be("HL");
        ZipFiles(_scratch.Inside("o.zip")).Should().Contain("h.txt=HL");
    }

    [RequiresSymlinkFact]
    public void Sdel_FileSymlinkInsideTheSource_TargetAndLinkStay()
    {
        string target = _scratch.WriteFile("outside.txt", "TARGET");
        File.CreateSymbolicLink(_scratch.Inside(@"src\sl.txt"), target);

        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "-sdel", "o.zip", "src");

        File.ReadAllText(target).Should().Be("TARGET");
        new FileInfo(_scratch.Inside(@"src\sl.txt")).LinkTarget.Should().NotBeNull("CLI.md: a link is never deleted");
        exitCode.Should().Be(1, stdErr);
    }

    // --- attributes, locks, ACLs ---

    // CLI.md: "read-only ones too".
    [Fact]
    public void Sdel_ReadOnlyFileAndFolder_AreArchivedThenDeleted()
    {
        File.SetAttributes(_scratch.Inside(@"src\a.txt"), FileAttributes.ReadOnly);
        var sub = new DirectoryInfo(_scratch.Inside(@"src\sub"));
        sub.Attributes |= FileAttributes.ReadOnly;

        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "-sdel", "o.zip", "src");

        exitCode.Should().Be(0, stdErr);
        ZipFiles(_scratch.Inside("o.zip")).Should().Equal("a.txt=A", "b.txt=B", "c.dat=C", "s.txt=S");
        Directory.Exists(_scratch.Inside("src")).Should().BeFalse();
    }

    [Fact]
    public void Sdel_SourceLockedAgainstReading_DeletesNothing()
    {
        int exitCode;
        string stdErr;
        using (new FileStream(_scratch.Inside(@"src\b.txt"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            (exitCode, _, stdErr) = _scratch.Pakko("a", "-sdel", "o.zip", "src");

        Tree("src").Should().Equal(FullSource);
        exitCode.Should().Be(2, stdErr);
    }

    // Readable, so archived, but held open without delete sharing: named, exit 2, the rest goes.
    [Fact]
    public void Sdel_SourceReadableButNotDeletable_IsNamedAndExitIsTwo()
    {
        int exitCode;
        string stdErr;
        using (new FileStream(_scratch.Inside(@"src\b.txt"), FileMode.Open, FileAccess.Read, FileShare.Read))
            (exitCode, _, stdErr) = _scratch.Pakko("a", "-sdel", "o.zip", "src");

        exitCode.Should().Be(2, stdErr);
        stdErr.Should().Contain("b.txt");
        Tree("src").Should().Equal("b.txt=B");
        ZipFiles(_scratch.Inside("o.zip")).Should().Equal("a.txt=A", "b.txt=B", "c.dat=C", "s.txt=S");
    }

    [Theory]
    [InlineData(@"src\b.txt", "R")]
    [InlineData(@"src\sub", "RD")]
    public void Sdel_AclDeniedInsideTheSource_DeletesNothing(string denied, string rights)
    {
        _scratch.Deny(denied, rights);

        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "-sdel", "o.zip", "src");

        exitCode.Should().Be(2, stdErr);
        File.Exists(_scratch.Inside(@"src\a.txt")).Should().BeTrue("one unreadable part keeps the whole source (CLI.md: an error anywhere, no source is deleted)");
        File.Exists(_scratch.Inside(@"src\c.dat")).Should().BeTrue();
    }

    // --- masks, filters, lists, the archive among its sources ---

    // CLI.md: "with a skip, an error or a -x filter anywhere, no source is deleted" (7-Zip
    // deletes the files it stored - and, measured on 26.02, b.txt that -x left out too).
    [Fact]
    public void Sdel_MaskWithExclude_DeletesNoSource()
    {
        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "-sdel", "o.zip", @"src\*.txt", "-x!b.txt");

        Tree("src").Should().Equal(FullSource);
        exitCode.Should().Be(1, stdErr);
    }

    [Fact]
    public void Sdel_RecursiveMask_KeepsWhatTheMaskDoesNotName()
    {
        (int exitCode, _, string stdErr) = _scratch.PakkoIn("src", "a", "-sdel", "-r", @"..\o.zip", "*.txt");

        exitCode.Should().Be(0, stdErr);
        File.ReadAllText(_scratch.Inside(@"src\c.dat")).Should().Be("C");
        ZipFiles(_scratch.Inside("o.zip")).Should().Equal("a.txt=A", "b.txt=B", "s.txt=S");
        File.Exists(_scratch.Inside(@"src\a.txt")).Should().BeFalse();
    }

    // -i adds names to the sources (7-Zip's rule); whatever goes must be in the archive.
    [Fact]
    public void Sdel_IncludeSwitch_EverythingDeletedIsInTheArchive()
    {
        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "-sdel", "o.zip", "src", "-i!*.txt");

        exitCode.Should().BeLessThan(2, stdErr);
        string[] archived = ZipFiles(_scratch.Inside("o.zip"));
        foreach (string file in FullSource.Where(e => e.Contains('=')))
        {
            string name = file.Split('\\')[^1];
            if (!File.Exists(_scratch.Inside(@"src\" + file.Split('=')[0])))
                archived.Should().Contain(name, $"{file} was deleted, so the archive must hold it");
        }
    }

    [Fact]
    public void Sdel_ListFile_DeletesTheListedSourcesOnly()
    {
        _scratch.WriteFile("list.txt", "src\\a.txt\r\nsrc\\sub\r\n");

        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "-sdel", "o.zip", "@list.txt");

        exitCode.Should().Be(0, stdErr);
        Tree("src").Should().Equal("b.txt=B", "c.dat=C");
        File.ReadAllText(_scratch.Inside("list.txt")).Should().Be("src\\a.txt\r\nsrc\\sub\r\n");
        ZipFiles(_scratch.Inside("o.zip")).Should().Equal("a.txt=A", "s.txt=S");
    }

    [Fact]
    public void Sdel_SourceGivenThreeWaysAndWithItsChild_ArchivedOnceThenDeleted()
    {
        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "-sdel", "o.zip", "src", @"src\", @".\SRC", @"src\a.txt");

        exitCode.Should().BeLessThan(2, stdErr);
        string[] archived = ZipFiles(_scratch.Inside("o.zip"));
        archived.Should().Contain(["a.txt=A", "b.txt=B", "c.dat=C", "s.txt=S"]);
        if (Directory.Exists(_scratch.Inside("src")))
            Tree("src").Should().BeSubsetOf(FullSource);
    }

    // The archive is written inside the folder being archived: the new archive must survive.
    [Fact]
    public void Sdel_ArchiveInsideTheSource_TheNewArchiveSurvives()
    {
        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "-sdel", @"src\o.zip", "src");

        string archive = _scratch.Inside(@"src\o.zip");
        File.Exists(archive).Should().BeTrue($"exit {exitCode}: {stdErr}");
        ZipFiles(archive).Should().Contain(["a.txt=A", "b.txt=B"]);
        exitCode.Should().BeLessThan(2, stdErr);
    }

    [Fact]
    public void Sdel_ArchiveNamedAsItsOwnSourceWithY_TheArchiveSurvives()
    {
        _scratch.WriteFile("o.zip", "OLD ARCHIVE BYTES");

        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "-sdel", "-y", "o.zip", "o.zip", "src");

        // T-F421: o.zip is a source, so the archive is numbered and holds it; -sdel then deletes it.
        string archive = _scratch.Inside("o (1).zip");
        File.Exists(archive).Should().BeTrue($"exit {exitCode}: {stdErr}");
        ZipFiles(archive).Should().Contain(["a.txt=A", "b.txt=B", "o.zip=OLD ARCHIVE BYTES"]);
        exitCode.Should().Be(1, stdErr);
    }

    [RequiresTarExe]
    public void Sdel_TarGz_StreamOfTarHoldsEverythingDeleted()
    {
        (int exitCode, _, string stdErr) = _scratch.Pakko("a", "-sdel", "-ttar.gz", "o.tar.gz", "src");

        exitCode.Should().Be(0, stdErr);
        Directory.Exists(_scratch.Inside("src")).Should().BeFalse();
        (int checkCode, _, string checkErr) = _scratch.Pakko("x", "o.tar.gz", "-ocheck");
        checkCode.Should().Be(0, checkErr);
        Tree(@"check\src").Should().Equal(FullSource);
    }

    // -sse and -sdel: the archive is not written, so nothing may go.
    [Fact]
    public void Sdel_WithSse_UnreadableSource_DeletesNothing()
    {
        int exitCode;
        string stdErr;
        using (new FileStream(_scratch.Inside(@"src\b.txt"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            (exitCode, _, stdErr) = _scratch.Pakko("a", "-sdel", "-sse", "o.zip", "src");

        exitCode.Should().Be(2, stdErr);
        Tree("src").Should().Equal(FullSource);
        File.Exists(_scratch.Inside("o.zip")).Should().BeFalse();
    }

    // CLI.md: "A file changed meanwhile stays (... exit 1)". The file's time moves forward while
    // the archive is still being written: whichever bytes went in, the file must stay.
    [Fact]
    [Trait("Category", "Slow")]
    public async Task Sdel_FileChangedWhileArchiving_IsKept()
    {
        for (int i = 0; i < 6; i++)
            File.WriteAllBytes(_scratch.Inside($@"src\big{i}.bin"), RandomNumberGenerator.GetBytes(16 * 1024 * 1024));
        string changed = _scratch.Inside(@"src\a.txt");
        string dest = _scratch.Folder("dest");

        var startInfo = new ProcessStartInfo(CliProcessRunner.ExePath)
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = _scratch.Root,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        string[] args = ["a", "-sdel", "-mmt1", "-mx9", @"dest\o.zip", "src"];
        foreach (string arg in args)
            startInfo.ArgumentList.Add(arg);
        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("pakko did not start");
        process.StandardInput.Close();
        Task<string> stdOut = process.StandardOutput.ReadToEndAsync();
        Task<string> stdErr = process.StandardError.ReadToEndAsync();

        while (!Directory.EnumerateFileSystemEntries(dest).Any())
        {
            process.HasExited.Should().BeFalse("harness: pakko finished before its temporary archive was seen; make the sources bigger");
            await Task.Delay(5);
        }
        File.SetLastWriteTimeUtc(changed, DateTime.UtcNow.AddHours(1));
        process.HasExited.Should().BeFalse("harness: the change must land before -sdel runs");
        await process.WaitForExitAsync();
        await stdOut;
        string errors = await stdErr;

        File.Exists(changed).Should().BeTrue($"a.txt changed after it was listed; CLI.md keeps it (stderr: {errors})");
        File.ReadAllText(changed).Should().Be("A");
        process.ExitCode.Should().Be(1, errors);
        errors.Should().Contain("a.txt").And.Contain("changed");
    }

    private (int ExitCode, int BytesRead, string StdErr) PakkoStdoutReadingOnly(int bytes, params string[] args)
    {
        using Process process = StartPakko(args);
        Task<string> stdErr = process.StandardError.ReadToEndAsync();
        byte[] buffer = new byte[bytes];
        int read = process.StandardOutput.BaseStream.ReadAtLeast(buffer, bytes, throwOnEndOfStream: false);
        process.StandardOutput.Close();
        process.WaitForExit();
        return (process.ExitCode, read, stdErr.GetAwaiter().GetResult());
    }

    private (int ExitCode, byte[] StdOut, string StdErr) PakkoStdoutReadAll(params string[] args)
    {
        using Process process = StartPakko(args);
        Task<string> stdErr = process.StandardError.ReadToEndAsync();
        using var stdOut = new MemoryStream();
        process.StandardOutput.BaseStream.CopyTo(stdOut);
        process.WaitForExit();
        return (process.ExitCode, stdOut.ToArray(), stdErr.GetAwaiter().GetResult());
    }

    private Process StartPakko(string[] args)
    {
        var startInfo = new ProcessStartInfo(CliProcessRunner.ExePath)
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = _scratch.Root,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (string arg in args)
            startInfo.ArgumentList.Add(arg);
        Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("pakko did not start");
        process.StandardInput.Close();
        return process;
    }
}

/// <summary>GetShortPathNameW: the 8.3 spelling of an existing path, or the path itself when the volume makes none.</summary>
internal static partial class ShortNames
{
    public static string Of(string path)
    {
        char[] buffer = new char[1024];
        uint length = GetShortPathNameW(path, buffer, (uint)buffer.Length);
        if (length == 0 || length >= buffer.Length)
            throw new IOException($"GetShortPathNameW failed for {path}: {Marshal.GetLastPInvokeError()}");
        return new string(buffer, 0, (int)length);
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial uint GetShortPathNameW(string longPath, [Out] char[] shortPath, uint bufferLength);
}

/// <summary>A test that needs 8.3 short names on the temp volume; skipped where none are made.</summary>
public sealed class RequiresShortNamesFactAttribute : FactAttribute
{
    public RequiresShortNamesFactAttribute()
    {
        string probe = Path.Combine(Path.GetTempPath(), "pakko-redteam-shortname-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(probe);
        try
        {
            if (ShortNames.Of(probe) == probe)
                Skip = "the temp volume makes no 8.3 short names";
        }
        catch (IOException ex)
        {
            Skip = "GetShortPathNameW failed: " + ex.Message;
        }
        finally
        {
            Directory.Delete(probe);
        }
    }
}
