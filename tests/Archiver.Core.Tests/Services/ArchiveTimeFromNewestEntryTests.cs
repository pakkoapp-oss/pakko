using Archiver.Core.Interfaces;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// A tar-family engine that writes a placeholder archive, so the router's own step is tested
// without tar.exe.
file sealed class WritingTarService : ITarService
{
    public Task<ArchiveResult> CompressAsync(ArchiveOptions options, IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default)
    {
        string path = Path.Combine(options.DestinationFolder, options.ArchiveName + ".tar");
        File.WriteAllText(path, "tar");
        return Task.FromResult(new ArchiveResult { CreatedFiles = [path] });
    }

    public Task<ArchiveResult> TestAsync(IReadOnlyList<string> archivePaths, IProgress<ProgressReport>? progress = null, Func<string, bool, bool>? entryFilter = null, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
    public Task<bool> CanCreateSevenZipAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
    public Task<TarCapabilities> DetectCapabilitiesAsync() => Task.FromResult(new TarCapabilities());
    public Task<ArchiveResult> ExtractAsync(ExtractOptions options, IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
    public Task<ArchiveListResult> ListEntriesAsync(string archivePath, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}

/// <summary>
/// T-F411: <see cref="ArchiveOptions.SetArchiveTimeFromNewestEntry"/> (7-Zip's -stl). Measured on
/// 7za 26.02: the archive takes the newest modification time of what it holds, folders included.
/// </summary>
public sealed class ArchiveTimeFromNewestEntryTests : IDisposable
{
    private static readonly DateTime FileTime = new(2020, 1, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime NewerFileTime = new(2021, 1, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime FolderTime = new(2023, 1, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime SourceFolderTime = new(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc);

    private readonly TempDirectory _temp = new();
    private readonly GroupPolicyOptions _policy = new();

    public void Dispose() => _temp.Dispose();

    private ArchiveCreationRouter Router() => new(new ZipArchiveService(_policy), new WritingTarService(), _policy);

    // src\a.txt (2020), src\d\b.txt (2021), src\d (2023), src (2024), set deepest first.
    private string CreateSource()
    {
        string src = Path.Combine(_temp.Path, "src");
        Directory.CreateDirectory(Path.Combine(src, "d"));
        File.WriteAllText(Path.Combine(src, "a.txt"), "a");
        File.WriteAllText(Path.Combine(src, "d", "b.txt"), "b");
        File.SetLastWriteTimeUtc(Path.Combine(src, "a.txt"), FileTime);
        File.SetLastWriteTimeUtc(Path.Combine(src, "d", "b.txt"), NewerFileTime);
        Directory.SetLastWriteTimeUtc(Path.Combine(src, "d"), FolderTime);
        Directory.SetLastWriteTimeUtc(src, SourceFolderTime);
        return src;
    }

    private string Destination()
    {
        string dest = Path.Combine(_temp.Path, "out");
        Directory.CreateDirectory(dest);
        return dest;
    }

    [Theory]
    [InlineData(ArchiveContainerFormat.Zip)]
    [InlineData(ArchiveContainerFormat.Tar)]
    public async Task ArchiveAsync_NewestIsTheSourceFolder_ArchiveTakesItsTime(ArchiveContainerFormat format)
    {
        string src = CreateSource();

        ArchiveResult result = await Router().ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [src], DestinationFolder = Destination(), ArchiveName = "a", Format = format,
            SetArchiveTimeFromNewestEntry = true,
        });

        result.CreatedFiles.Should().ContainSingle();
        File.GetLastWriteTimeUtc(result.CreatedFiles[0]).Should().Be(SourceFolderTime);
    }

    [Fact]
    public async Task ArchiveAsync_FilesOnly_ArchiveTakesTheNewestFilesTime()
    {
        string src = CreateSource();

        ArchiveResult result = await Router().ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [Path.Combine(src, "a.txt"), Path.Combine(src, "d", "b.txt")],
            DestinationFolder = Destination(), ArchiveName = "a", SetArchiveTimeFromNewestEntry = true,
        });

        File.GetLastWriteTimeUtc(result.CreatedFiles[0]).Should().Be(NewerFileTime);
    }

    [Fact]
    public async Task ArchiveAsync_WithoutTheOption_ArchiveKeepsTheTimeItWasWritten()
    {
        string src = CreateSource();
        DateTime before = DateTime.UtcNow.AddMinutes(-1);

        ArchiveResult result = await Router().ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [src], DestinationFolder = Destination(), ArchiveName = "a",
        });

        File.GetLastWriteTimeUtc(result.CreatedFiles[0]).Should().BeAfter(before);
    }

    // What the filter leaves out is not in the archive, so its time does not count.
    [Fact]
    public async Task ArchiveAsync_NewestFileLeftOut_DoesNotCount()
    {
        string src = CreateSource();
        Directory.SetLastWriteTimeUtc(Path.Combine(src, "d"), FileTime);
        Directory.SetLastWriteTimeUtc(src, FileTime);

        ArchiveResult result = await Router().ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [src], DestinationFolder = Destination(), ArchiveName = "a",
            EntryFilter = (name, _) => name != "src/d/b.txt",
            SetArchiveTimeFromNewestEntry = true,
        });

        File.GetLastWriteTimeUtc(result.CreatedFiles[0]).Should().Be(FileTime);
    }

    // A junction is never entered by the walk Core archives with, so a newer file behind it is not counted.
    [Fact]
    public async Task ArchiveAsync_NewerFileBehindAJunction_DoesNotCount()
    {
        string src = CreateSource();
        string outside = Path.Combine(_temp.Path, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "late.txt"), "late");
        string junction = Path.Combine(src, "j");
        if (!ZipArchiveServiceArchiveTests.TryCreateJunction(junction, outside))
            return; // no junctions on this volume
        try
        {
            Directory.SetLastWriteTimeUtc(src, FileTime);
            Directory.SetLastWriteTimeUtc(Path.Combine(src, "d"), FileTime);

            ArchiveResult result = await Router().ArchiveAsync(new ArchiveOptions
            {
                SourcePaths = [src], DestinationFolder = Destination(), ArchiveName = "a", SetArchiveTimeFromNewestEntry = true,
            });

            File.GetLastWriteTimeUtc(result.CreatedFiles[0]).Should().Be(NewerFileTime);
        }
        finally
        {
            Directory.Delete(junction); // the link only; the temp folder's recursive delete cannot remove it
        }
    }

    // The option names one archive's time from all sources; SeparateArchives leaves each archive's own.
    [Fact]
    public async Task ArchiveAsync_SeparateArchives_TimesAreUntouched()
    {
        string src = CreateSource();
        DateTime before = DateTime.UtcNow.AddMinutes(-1);

        ArchiveResult result = await Router().ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [src], DestinationFolder = Destination(), Mode = ArchiveMode.SeparateArchives,
            SetArchiveTimeFromNewestEntry = true,
        });

        result.CreatedFiles.Should().ContainSingle();
        File.GetLastWriteTimeUtc(result.CreatedFiles[0]).Should().BeAfter(before);
    }
}
