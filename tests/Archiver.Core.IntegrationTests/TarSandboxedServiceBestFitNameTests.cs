using System.Text;
using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

// T-F441/T-F424: tar.exe lists a name outside the user's ANSI page by best fit (an e-acute "cafe"
// as "cafe" on 1251, an a-macron "cafa" as "cafa" on 1252; bsdtar 3.8.9 and 3.5.2 alike), so an
// archive holding both names lists one line twice. Before T-F441 selecting either row extracted
// the plain file with success, and a filter that left out "cafe.txt" still wrote the accented one.
// The archive is a plain tar or a tar.gz: the only kind whose headers Pakko reads to see it. Each
// test returns early on a machine whose page carries both candidate names (UTF-8, for one).
[Collection("TarSandbox")]
public sealed class TarSandboxedServiceBestFitNameTests : IDisposable
{
    private readonly TarSandboxedService _sut = new(new GroupPolicyOptions());
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Integration]
    public async Task Extract_SelectingTheCollapsedName_RefusedNothingWritten()
    {
        foreach (bool gzip in new[] { false, true })
        {
            if (await CollapsingArchiveAsync(gzip) is not { } fixture)
                return;
            string dest = NewDestination();

            ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
            {
                ArchivePaths = [fixture.Path], DestinationFolder = dest, Mode = ExtractMode.SingleFolder,
                SelectedEntryPaths = [fixture.Plain],
            });

            result.Success.Should().BeFalse(TarCodePage.Describe());
            // Extraction wraps the refusal (CannotExtractArchive), as T-F416's does; its text names the entry.
            result.Errors.Should().ContainSingle().Which.Message.Should().Contain($"'{fixture.Plain}'").And.Contain("tar.exe");
            WrittenFiles(dest).Should().BeEmpty();
        }
    }

    [Integration]
    public async Task Extract_FilterOverTheCollapsedName_RefusedNothingWritten()
    {
        foreach (bool gzip in new[] { false, true })
        {
            if (await CollapsingArchiveAsync(gzip) is not { } fixture)
                return;
            string dest = NewDestination();

            ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
            {
                ArchivePaths = [fixture.Path], DestinationFolder = dest, Mode = ExtractMode.SingleFolder,
                EntryFilter = (path, _) => path != fixture.Plain,
            });

            result.Success.Should().BeFalse(TarCodePage.Describe());
            result.Errors.Should().ContainSingle().Which.Message.Should().Contain($"'{fixture.Plain}'").And.Contain("tar.exe");
            WrittenFiles(dest).Should().BeEmpty();
        }
    }

    // Nothing decided by name: both files arrive under their true names, as before.
    [Integration]
    public async Task Extract_Everything_BothFilesUnderTheirTrueNames()
    {
        foreach (bool gzip in new[] { false, true })
        {
            if (await CollapsingArchiveAsync(gzip) is not { } fixture)
                return;
            string dest = NewDestination();

            ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
            {
                ArchivePaths = [fixture.Path], DestinationFolder = dest, Mode = ExtractMode.SingleFolder,
            });

            result.Errors.Should().BeEmpty(TarCodePage.Describe());
            // Before T-F441 the two lines made a false duplicate group: "copies not extracted".
            result.SkippedFiles.Should().BeEmpty(TarCodePage.Describe());
            File.ReadAllText(Path.Combine(dest, fixture.Accented)).Should().Be("ACCENT");
            File.ReadAllText(Path.Combine(dest, fixture.Plain)).Should().Be("PLAIN");
        }
    }

    // Folders are pre-created for the sandbox (T-F52), and folder entries and times applied, by
    // listed name: a folder listed by best fit must not leave a second, empty folder under that name.
    // Measured 2026-10-31 (bsdtar 3.8.9, ACP 1251): the destination held "cafe", "cafe/empty" and
    // "caf<e-acute>" with x.txt - the files right, the empty folder under the listed name. Fixed by T-F443.
    [Integration]
    public async Task Extract_Everything_FolderListedByBestFit_OnlyTheTrueFolder()
    {
        foreach ((char accent, char plain) in new[] { ((char)0xE9, 'e'), ((char)0x101, 'a') })
        {
            string folder = "caf" + accent;
            string path = Path.Combine(_temp.Path, $"dir-{(int)accent:x}.tar");
            TarBuilder.WriteTar(path,
            [
                TarBuilder.PaxPath(folder + "/"),
                new TarBuilder.Entry { Name = "placeholder/", TypeFlag = '5' },
                TarBuilder.PaxPath(folder + "/x.txt"),
                new TarBuilder.Entry { Name = "placeholder.txt", Content = Encoding.ASCII.GetBytes("X") },
                TarBuilder.PaxPath(folder + "/empty/"),
                new TarBuilder.Entry { Name = "placeholder2/", TypeFlag = '5' },
            ]);
            ArchiveListResult list = await _sut.ListEntriesAsync(path);
            if (!list.Success || !list.Entries.Any(e => e.Path.StartsWith("caf" + plain, StringComparison.Ordinal)))
                continue; // this page carries the name
            string dest = NewDestination();

            ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
            {
                ArchivePaths = [path], DestinationFolder = dest, Mode = ExtractMode.SingleFolder,
            });

            result.Errors.Should().BeEmpty(TarCodePage.Describe());
            File.ReadAllText(Path.Combine(dest, folder, "x.txt")).Should().Be("X");
            Directory.GetDirectories(dest, "*", SearchOption.AllDirectories)
                .Select(d => Path.GetRelativePath(dest, d).Replace('\\', '/'))
                .Should().BeEquivalentTo([folder, folder + "/empty"], TarCodePage.Describe());
            return;
        }
    }

    [Integration]
    public async Task Test_FilterOverTheCollapsedName_Refused()
    {
        if (await CollapsingArchiveAsync(gzip: false) is not { } fixture)
            return;

        ArchiveResult result = await _sut.TestAsync([fixture.Path], entryFilter: (path, _) => path != fixture.Plain);

        result.Success.Should().BeFalse(TarCodePage.Describe());
        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.TarListedNameUntrusted);
    }

    // T-F443: a folder with no entry of its own - tar.exe makes the true one itself (bsdtar 3.8.9 in
    // the AppContainer, 2026-11-05), and the folder Pakko pre-created by the listed name stays behind.
    [Integration]
    public async Task Extract_Everything_ImplicitFolderListedByBestFit_OnlyTheTrueFolder()
    {
        if (await BestFitFolderArchiveAsync("implicit", f =>
            [TarBuilder.PaxPath(f + "/x.txt"), new TarBuilder.Entry { Name = "placeholder.txt", Content = Encoding.ASCII.GetBytes("X") }])
            is not { } fixture)
            return;
        string dest = NewDestination();

        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [fixture.Path], DestinationFolder = dest, Mode = ExtractMode.SingleFolder,
        });

        result.Errors.Should().BeEmpty(TarCodePage.Describe());
        Everything(dest).Should().BeEquivalentTo([fixture.Folder, fixture.Folder + "/x.txt"], TarCodePage.Describe());
    }

    // T-F443: the folder's entry after its content, so Pakko pre-created the folder by its listed
    // name and that name is also a listed folder entry; the twin must still not arrive.
    [Integration]
    public async Task Extract_Everything_FolderEntryAfterItsContent_OnlyTheTrueFolder()
    {
        if (await BestFitFolderArchiveAsync("after", f =>
            [
                TarBuilder.PaxPath(f + "/x.txt"),
                new TarBuilder.Entry { Name = "placeholder.txt", Content = Encoding.ASCII.GetBytes("X") },
                TarBuilder.PaxPath(f + "/"),
                new TarBuilder.Entry { Name = "placeholder/", TypeFlag = '5' },
                TarBuilder.PaxPath(f + "/empty/"),
                new TarBuilder.Entry { Name = "placeholder2/", TypeFlag = '5' },
            ]) is not { } fixture)
            return;
        string dest = NewDestination();

        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [fixture.Path], DestinationFolder = dest, Mode = ExtractMode.SingleFolder,
        });

        result.Errors.Should().BeEmpty(TarCodePage.Describe());
        Everything(dest).Should().BeEquivalentTo(
            [fixture.Folder, fixture.Folder + "/x.txt", fixture.Folder + "/empty"], TarCodePage.Describe());
    }

    // T-F443: the root dropped as the archive's own name (decided on the listed name), so a folder
    // from the quarantine loses its root exactly as the files do.
    [Integration]
    public async Task Extract_Everything_BestFitRootDroppedAsTheArchiveName_FoldersStrippedLikeFiles()
    {
        if (await BestFitFolderArchiveAsync("root", f =>
            [
                TarBuilder.PaxPath(f + "/"),
                new TarBuilder.Entry { Name = "placeholder/", TypeFlag = '5' },
                TarBuilder.PaxPath(f + "/x.txt"),
                new TarBuilder.Entry { Name = "placeholder.txt", Content = Encoding.ASCII.GetBytes("X") },
                TarBuilder.PaxPath(f + "/empty/"),
                new TarBuilder.Entry { Name = "placeholder2/", TypeFlag = '5' },
            ]) is not { } fixture)
            return;
        string named = Path.Combine(_temp.Path, fixture.ListedFolder + ".tar");
        File.Copy(fixture.Path, named);
        string dest = NewDestination();

        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [named], DestinationFolder = dest, Mode = ExtractMode.SingleFolder,
            EliminateDuplicateRootFolder = true,
        });

        result.Errors.Should().BeEmpty(TarCodePage.Describe());
        Everything(dest).Should().BeEquivalentTo(["x.txt", "empty"], TarCodePage.Describe());
    }

    // A filter decides on folder names too; a folder listed by best fit makes them untrustworthy.
    [Integration]
    public async Task Extract_FilterOverABestFitFolder_RefusedNothingWritten()
    {
        if (await BestFitFolderArchiveAsync("filter", f =>
            [
                TarBuilder.PaxPath(f + "/"),
                new TarBuilder.Entry { Name = "placeholder/", TypeFlag = '5' },
                new TarBuilder.Entry { Name = "plain.txt", Content = Encoding.ASCII.GetBytes("P") },
            ]) is not { } fixture)
            return;
        string dest = NewDestination();

        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [fixture.Path], DestinationFolder = dest, Mode = ExtractMode.SingleFolder,
            EntryFilter = (path, _) => path != "other.txt",
        });

        result.Success.Should().BeFalse(TarCodePage.Describe());
        WrittenFiles(dest).Should().BeEmpty();
    }

    // T-F443 review: a folder Pakko pre-creates by a name Windows trims ("d." made as "d") is not
    // evidence about tar.exe's names; a faithful archive with a filter or a real duplicate still extracts.
    // T-F454: a trailing space too ("d " beside the "d" Pakko made).
    [TarIntegrationTheory]
    [InlineData("d.")]
    [InlineData("d ")]
    [InlineData("d. ")]
    public async Task Extract_ImplicitFolderWindowsTrims_FilterAndDuplicateStillWork(string folder)
    {
        string path = Path.Combine(_temp.Path, $"trim-{folder.Length}-{(int)folder[^1]:x}.tar");
        TarBuilder.WriteTar(path,
        [
            new TarBuilder.Entry { Name = folder + "/x.txt", Content = Encoding.ASCII.GetBytes("1") },
            new TarBuilder.Entry { Name = "y.txt", Content = Encoding.ASCII.GetBytes("A") },
            new TarBuilder.Entry { Name = "y.txt", Content = Encoding.ASCII.GetBytes("B") },
        ]);

        ArchiveResult filtered = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [path], DestinationFolder = NewDestination(), Mode = ExtractMode.SingleFolder,
            EntryFilter = (_, _) => true,
        });
        string dest = NewDestination();
        ArchiveResult everything = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [path], DestinationFolder = dest, Mode = ExtractMode.SingleFolder,
            OnConflict = ConflictBehavior.Rename,
        });

        filtered.Errors.Should().BeEmpty(string.Join(" | ", filtered.Errors.Select(e => e.Message)));
        filtered.Success.Should().BeTrue();
        everything.Errors.Should().BeEmpty(TarCodePage.Describe());
        // The duplicate group stands (T-F171): the first copy takes the name, the last one is renamed.
        File.ReadAllText(Path.Combine(dest, "y.txt")).Should().Be("A", TarCodePage.Describe());
        File.ReadAllText(Path.Combine(dest, "y (1).txt")).Should().Be("B", TarCodePage.Describe());
        File.ReadAllText(Path.Combine(dest, "d", "x.txt")).Should().Be("1");
    }

    // T-F454: a folder entry ending in a space, before its file: tar.exe makes "d " itself, and
    // the file lands in it; the destination gets "d" (Windows' name for it), with or without a filter.
    [TarIntegrationTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Extract_ExplicitFolderEndingInSpace_Arrives(bool filter)
    {
        string path = Path.Combine(_temp.Path, $"space-entry-{filter}.tar");
        TarBuilder.WriteTar(path,
        [
            new TarBuilder.Entry { Name = "d /", TypeFlag = '5' },
            new TarBuilder.Entry { Name = "d /x.txt", Content = Encoding.ASCII.GetBytes("1") },
        ]);
        string dest = NewDestination();

        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [path], DestinationFolder = dest, Mode = ExtractMode.SingleFolder,
            EntryFilter = filter ? (_, _) => true : null,
        });

        result.Errors.Should().BeEmpty(string.Join(" | ", result.Errors.Select(e => e.Message)));
        File.ReadAllText(Path.Combine(dest, "d", "x.txt")).Should().Be("1");
    }

    // T-F460: an explicit folder entry ending in a dot is made by tar.exe as "d."; its file arrives
    // under Windows' name for it.
    [Integration]
    public async Task Extract_ExplicitFolderEndingInDot_Arrives()
    {
        string path = Path.Combine(_temp.Path, "dot-entry.tar");
        TarBuilder.WriteTar(path,
        [
            new TarBuilder.Entry { Name = "d./", TypeFlag = '5' },
            new TarBuilder.Entry { Name = "d./x.txt", Content = Encoding.ASCII.GetBytes("1") },
        ]);
        string dest = NewDestination();

        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [path], DestinationFolder = dest, Mode = ExtractMode.SingleFolder,
        });

        result.Errors.Should().BeEmpty(string.Join(" | ", result.Errors.Select(e => e.Message)));
        File.ReadAllText(Path.Combine(dest, "d", "x.txt")).Should().Be("1");
    }

    // T-F454/T-F461: "d ./x.txt" - Pakko makes "d " (tar.exe's name for the file's folder), and
    // tar.exe also makes "d" asking Windows for "d .". Everything arrives in "d"; a filter is
    // refused, since the quarantine holds a folder the listing does not name (T-F461).
    [Integration]
    public async Task Extract_ImplicitFolderEndingInSpaceDot_ArrivesWithoutFilter_RefusedWithOne()
    {
        string path = Path.Combine(_temp.Path, "space-dot.tar");
        TarBuilder.WriteTar(path, [new TarBuilder.Entry { Name = "d ./x.txt", Content = Encoding.ASCII.GetBytes("1") }]);
        string all = NewDestination();
        string filtered = NewDestination();

        ArchiveResult everything = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [path], DestinationFolder = all, Mode = ExtractMode.SingleFolder,
        });
        ArchiveResult withFilter = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [path], DestinationFolder = filtered, Mode = ExtractMode.SingleFolder,
            EntryFilter = (_, _) => true,
        });

        everything.Errors.Should().BeEmpty(string.Join(" | ", everything.Errors.Select(e => e.Message)));
        File.ReadAllText(Path.Combine(all, "d", "x.txt")).Should().Be("1");
        withFilter.Success.Should().BeFalse();
        WrittenFiles(filtered).Should().BeEmpty();
    }

    // T-F454: "d/x.txt" and "d /x.txt" are two files in the quarantine ("d" and "d "); both land in
    // "d", so the trimmed one is kept apart under a new name (T-F419), never written over the first.
    [Integration]
    public async Task Extract_FileInDAndInDSpace_BothArrive()
    {
        string path = Path.Combine(_temp.Path, "d-and-d-space.tar");
        TarBuilder.WriteTar(path,
        [
            new TarBuilder.Entry { Name = "d/x.txt", Content = Encoding.ASCII.GetBytes("A") },
            new TarBuilder.Entry { Name = "d /x.txt", Content = Encoding.ASCII.GetBytes("B") },
        ]);
        string dest = NewDestination();

        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [path], DestinationFolder = dest, Mode = ExtractMode.SingleFolder,
            OnConflict = ConflictBehavior.Overwrite,
        });

        result.Errors.Should().BeEmpty(string.Join(" | ", result.Errors.Select(e => e.Message)));
        // As ZIP: the plain "d/x.txt" keeps the name, the trimmed twin is kept apart.
        File.ReadAllText(Path.Combine(dest, "d", "x.txt")).Should().Be("A");
        File.ReadAllText(Path.Combine(dest, "d", "x (1).txt")).Should().Be("B");
    }

    // T-F460 (fresh review): the first of two same-named copies under "d /" comes from a second pass
    // (T-F171), which must make "d " by its own name too, or that copy is reported lost.
    [Integration]
    public async Task Extract_TwoCopiesInAFolderEndingInSpace_BothArrive()
    {
        string path = Path.Combine(_temp.Path, "twice-space.tar");
        TarBuilder.WriteTar(path,
        [
            new TarBuilder.Entry { Name = "d /", TypeFlag = '5' },
            new TarBuilder.Entry { Name = "d /x.txt", Content = Encoding.ASCII.GetBytes("A") },
            new TarBuilder.Entry { Name = "d /x.txt", Content = Encoding.ASCII.GetBytes("B") },
        ]);
        string dest = NewDestination();

        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [path], DestinationFolder = dest, Mode = ExtractMode.SingleFolder,
            OnConflict = ConflictBehavior.Overwrite,
        });

        result.Errors.Should().BeEmpty(string.Join(" | ", result.Errors.Select(e => e.Message)));
        string[] texts = [.. Directory.GetFiles(Path.Combine(dest, "d")).Select(File.ReadAllText).Order()];
        texts.Should().Equal("A", "B");
    }

    // T-F462: an accented name stored twice lists as its best-fit name twice; T-F441 drops the
    // duplicate group (the listing cannot tell the copies apart), so the first copy never comes out.
    // Before, the archive reported success without it; now nothing is written and the run says why.
    [Integration]
    public async Task Extract_AccentedNameTwice_FailsWithTheCountNothingWritten()
    {
        foreach ((char accent, char plain) in new[] { ((char)0xE9, 'e'), ((char)0x101, 'a') })
        {
            string accented = "caf" + accent + ".txt";
            string path = Path.Combine(_temp.Path, $"twice-{(int)accent:x}.tar");
            TarBuilder.WriteTar(path,
            [
                TarBuilder.PaxPath(accented),
                new TarBuilder.Entry { Name = "placeholder.txt", Content = Encoding.ASCII.GetBytes("first") },
                TarBuilder.PaxPath(accented),
                new TarBuilder.Entry { Name = "placeholder.txt", Content = Encoding.ASCII.GetBytes("last") },
            ]);
            ArchiveListResult list = await _sut.ListEntriesAsync(path);
            if (!list.Success || list.Entries.Count != 2 || !list.Entries.All(e => e.Path == "caf" + plain + ".txt"))
                continue;
            string dest = NewDestination();

            ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
            {
                ArchivePaths = [path], DestinationFolder = dest, Mode = ExtractMode.SingleFolder,
            });

            result.Success.Should().BeFalse(TarCodePage.Describe());
            result.Errors.Should().ContainSingle().Which.Message.Should().Contain("1 of 2");
            WrittenFiles(dest).Should().BeEmpty();
            return;
        }
    }

    // T-F462 (fresh review, accepted fail-closed): a name listed by best fit drops every duplicate
    // group (T-F441), so the first "y.txt" never comes out and nothing is written. Per-group
    // judgment is T-F464's.
    [Integration]
    public async Task Extract_BestFitNameBesideADuplicateGroup_FailsNothingWritten()
    {
        foreach ((char accent, char plain) in new[] { ((char)0xE9, 'e'), ((char)0x101, 'a') })
        {
            string path = Path.Combine(_temp.Path, $"beside-group-{(int)accent:x}.tar");
            TarBuilder.WriteTar(path,
            [
                TarBuilder.PaxPath("caf" + accent + ".txt"),
                new TarBuilder.Entry { Name = "placeholder.txt", Content = Encoding.ASCII.GetBytes("cafe") },
                new TarBuilder.Entry { Name = "y.txt", Content = Encoding.ASCII.GetBytes("one") },
                new TarBuilder.Entry { Name = "y.txt", Content = Encoding.ASCII.GetBytes("two") },
            ]);
            ArchiveListResult list = await _sut.ListEntriesAsync(path);
            if (!list.Success || !list.Entries.Any(e => e.Path == "caf" + plain + ".txt"))
                continue;
            string dest = NewDestination();

            ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
            {
                ArchivePaths = [path], DestinationFolder = dest, Mode = ExtractMode.SingleFolder,
            });

            result.Success.Should().BeFalse(TarCodePage.Describe());
            result.Errors.Should().ContainSingle().Which.Message.Should().Contain("2 of 3");
            WrittenFiles(dest).Should().BeEmpty();
            return;
        }
    }

    // T-F462 (fresh review): tar.exe replaces the file "x" with the folder "x/" listed after it and
    // exits 0 (bsdtar 3.8.9); the file is lost, so nothing is written and the run says why.
    [Integration]
    public async Task Extract_FileReplacedByAFolderOfItsName_FailsNothingWritten()
    {
        string path = Path.Combine(_temp.Path, "file-then-folder.tar");
        TarBuilder.WriteTar(path,
        [
            new TarBuilder.Entry { Name = "x", Content = Encoding.ASCII.GetBytes("file") },
            new TarBuilder.Entry { Name = "x/", TypeFlag = '5' },
            new TarBuilder.Entry { Name = "y.txt", Content = Encoding.ASCII.GetBytes("y") },
        ]);
        string dest = NewDestination();

        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [path], DestinationFolder = dest, Mode = ExtractMode.SingleFolder,
        });

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Message.Should().Contain("1 of 2");
        WrittenFiles(dest).Should().BeEmpty();
    }

    // T-F461: a file before its own folder entry ending in a space fails in tar.exe itself on both
    // builds ("Can't restore time" once the folder exists, "Can't create" when it does not): the
    // run fails naming tar.exe, and nothing is written.
    [Integration]
    public async Task Extract_FileBeforeItsFolderEntryEndingInSpace_FailsWithNothingWritten()
    {
        string path = Path.Combine(_temp.Path, "space-late.tar");
        TarBuilder.WriteTar(path,
        [
            new TarBuilder.Entry { Name = "d /x.txt", Content = Encoding.ASCII.GetBytes("1") },
            new TarBuilder.Entry { Name = "d /", TypeFlag = '5' },
        ]);
        string dest = NewDestination();

        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [path], DestinationFolder = dest, Mode = ExtractMode.SingleFolder,
        });

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Message.Should().Contain("tar.exe");
        WrittenFiles(dest).Should().BeEmpty();
    }

    // T-F443 fresh review: Pakko pre-creates "cafe/sub" for the listed "cafe/sub/x.txt", and that
    // also makes "cafe" - a parent made along the way is Pakko's too, and no twin may arrive.
    [Integration]
    public async Task Extract_Everything_ParentMadeWhilePreCreating_NoTwin()
    {
        if (await BestFitFolderArchiveAsync("parent", f =>
            [
                TarBuilder.PaxPath(f + "/"),
                new TarBuilder.Entry { Name = "placeholder/", TypeFlag = '5' },
                TarBuilder.PaxPath(f + "/sub/x.txt"),
                new TarBuilder.Entry { Name = "placeholder.txt", Content = Encoding.ASCII.GetBytes("X") },
                new TarBuilder.Entry { Name = "readme.txt", Content = Encoding.ASCII.GetBytes("R") },
            ]) is not { } fixture)
            return;
        string dest = NewDestination();

        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [fixture.Path], DestinationFolder = dest, Mode = ExtractMode.SingleFolder,
        });

        result.Errors.Should().BeEmpty(TarCodePage.Describe());
        Everything(dest).Should().BeEquivalentTo(
            [fixture.Folder, fixture.Folder + "/sub", fixture.Folder + "/sub/x.txt", "readme.txt"], TarCodePage.Describe());
    }

    // T-F443 fresh review: a folder alone listed by best fit says nothing about two files of one
    // name; their duplicate group (T-F171) stands, so the first copy is not lost.
    [Integration]
    public async Task Extract_Everything_BestFitFolderBesideARealDuplicate_BothCopiesKept()
    {
        if (await BestFitFolderArchiveAsync("dup", f =>
            [
                TarBuilder.PaxPath(f + "/"),
                new TarBuilder.Entry { Name = "placeholder/", TypeFlag = '5' },
                new TarBuilder.Entry { Name = "y.txt", Content = Encoding.ASCII.GetBytes("A") },
                new TarBuilder.Entry { Name = "y.txt", Content = Encoding.ASCII.GetBytes("B") },
            ]) is not { } fixture)
            return;
        string dest = NewDestination();

        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [fixture.Path], DestinationFolder = dest, Mode = ExtractMode.SingleFolder,
            OnConflict = ConflictBehavior.Rename,
        });

        result.Errors.Should().BeEmpty(TarCodePage.Describe());
        File.ReadAllText(Path.Combine(dest, "y.txt")).Should().Be("A", TarCodePage.Describe());
        File.ReadAllText(Path.Combine(dest, "y (1).txt")).Should().Be("B", TarCodePage.Describe());
        Directory.GetDirectories(dest).Select(Path.GetFileName).Should().Equal([fixture.Folder], TarCodePage.Describe());
    }

    // T-F443 fresh review: a folder entry whose stored name has no trailing '/' (a type-5 header
    // "emptydir") must not read as a file the quarantine does not hold.
    [Integration]
    public async Task Extract_FilterOverAFolderEntryWithoutTrailingSlash_NotRefused()
    {
        string path = Path.Combine(_temp.Path, "noslash.tar");
        TarBuilder.WriteTar(path,
        [
            new TarBuilder.Entry { Name = "emptydir", TypeFlag = '5' },
            new TarBuilder.Entry { Name = "a.txt", Content = Encoding.ASCII.GetBytes("A") },
        ]);
        string dest = NewDestination();
        var asked = new List<(string Path, bool IsFolder)>();

        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [path], DestinationFolder = dest, Mode = ExtractMode.SingleFolder,
            EntryFilter = (entry, isFolder) => { asked.Add((entry, isFolder)); return true; },
        });

        result.Errors.Should().BeEmpty(string.Join(" | ", result.Errors.Select(e => e.Message)));
        File.ReadAllText(Path.Combine(dest, "a.txt")).Should().Be("A");
        // T-F455: the folder is asked about as a folder, and it arrives.
        asked.Should().Contain(("emptydir/", true));
        Directory.Exists(Path.Combine(dest, "emptydir")).Should().BeTrue();
    }

    // T-F455: folder entries stored without a trailing '/' (one empty, one with a file) arrive as
    // folders on a plain extraction, and the selected-folder path passes them back to tar.exe.
    [Integration]
    public async Task Extract_FolderEntriesWithoutTrailingSlash_Arrive()
    {
        string path = Path.Combine(_temp.Path, "noslash-all.tar");
        TarBuilder.WriteTar(path,
        [
            new TarBuilder.Entry { Name = "emptydir", TypeFlag = '5' },
            new TarBuilder.Entry { Name = "full", TypeFlag = '5' },
            new TarBuilder.Entry { Name = "full/x.txt", Content = Encoding.ASCII.GetBytes("X") },
        ]);
        string all = NewDestination();
        string selected = NewDestination();

        ArchiveResult everything = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [path], DestinationFolder = all, Mode = ExtractMode.SingleFolder,
        });
        ArchiveResult subset = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [path], DestinationFolder = selected, Mode = ExtractMode.SingleFolder,
            SelectedEntryPaths = ["emptydir"],
        });

        everything.Errors.Should().BeEmpty(string.Join(" | ", everything.Errors.Select(e => e.Message)));
        Everything(all).Should().BeEquivalentTo(["emptydir", "full", "full/x.txt"]);
        subset.Errors.Should().BeEmpty(string.Join(" | ", subset.Errors.Select(e => e.Message)));
        Everything(selected).Should().BeEquivalentTo(["emptydir"]);
    }

    private sealed record FolderFixture(string Path, string Folder, string ListedFolder);

    // The entries built around "caf<accent>"; null when this machine's page carries that name.
    private async Task<FolderFixture?> BestFitFolderArchiveAsync(string tag, Func<string, TarBuilder.Entry[]> entries)
    {
        foreach ((char accent, char plain) in new[] { ((char)0xE9, 'e'), ((char)0x101, 'a') })
        {
            string folder = "caf" + accent;
            string path = Path.Combine(_temp.Path, $"{tag}-{(int)accent:x}.tar");
            TarBuilder.WriteTar(path, entries(folder));
            ArchiveListResult list = await _sut.ListEntriesAsync(path);
            if (list.Success && list.Entries.Any(e => e.Path.StartsWith("caf" + plain, StringComparison.Ordinal)))
                return new FolderFixture(path, folder, "caf" + plain);
        }
        return null;
    }

    private static string[] Everything(string dest) =>
        [.. Directory.GetFileSystemEntries(dest, "*", SearchOption.AllDirectories).Select(d => Path.GetRelativePath(dest, d).Replace(Path.DirectorySeparatorChar, '/'))];

    private sealed record Fixture(string Path, string Accented, string Plain);

    // An archive with "caf<accent>.txt" (ACCENT) and "caf<base>.txt" (PLAIN) whose listing shows
    // the plain name twice on this machine; null when no candidate collapses here.
    private async Task<Fixture?> CollapsingArchiveAsync(bool gzip)
    {
        foreach ((char accent, char plain) in new[] { ((char)0xE9, 'e'), ((char)0x101, 'a') })
        {
            string accented = "caf" + accent + ".txt";
            string plainName = "caf" + plain + ".txt";
            string path = Path.Combine(_temp.Path, $"{(int)accent:x}-{gzip}.tar");
            TarBuilder.WriteTar(path,
            [
                TarBuilder.PaxPath(accented),
                new TarBuilder.Entry { Name = "placeholder.txt", Content = Encoding.ASCII.GetBytes("ACCENT") },
                new TarBuilder.Entry { Name = plainName, Content = Encoding.ASCII.GetBytes("PLAIN") },
            ]);
            if (gzip)
                path = GzipInPlace(path);

            ArchiveListResult list = await _sut.ListEntriesAsync(path);
            if (list.Success && list.Entries.Count == 2 && list.Entries.All(e => e.Path == plainName))
                return new Fixture(path, accented, plainName);
        }
        return null;
    }

    private string NewDestination() => Path.Combine(_temp.Path, "out-" + Guid.NewGuid().ToString("N"));

    private static string[] WrittenFiles(string dest) =>
        Directory.Exists(dest) ? Directory.GetFiles(dest, "*", SearchOption.AllDirectories) : [];

    private static string GzipInPlace(string tarPath)
    {
        string gzipPath = tarPath + ".gz";
        using (FileStream source = File.OpenRead(tarPath))
        using (FileStream target = File.Create(gzipPath))
        using (var gzip = new System.IO.Compression.GZipStream(target, System.IO.Compression.CompressionLevel.Optimal))
            source.CopyTo(gzip);
        File.Delete(tarPath);
        return gzipPath;
    }
}
