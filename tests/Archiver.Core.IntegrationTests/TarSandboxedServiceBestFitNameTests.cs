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
            result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.TarNameNotRepresentable);
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

    [Integration]
    public async Task Test_FilterOverTheCollapsedName_Refused()
    {
        if (await CollapsingArchiveAsync(gzip: false) is not { } fixture)
            return;

        ArchiveResult result = await _sut.TestAsync([fixture.Path], entryFilter: (path, _) => path != fixture.Plain);

        result.Success.Should().BeFalse(TarCodePage.Describe());
        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.TarNameNotRepresentable);
    }

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
