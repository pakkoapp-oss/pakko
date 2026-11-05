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
        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.TarNameNotRepresentable);
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
