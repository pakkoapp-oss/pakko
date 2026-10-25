using System.Formats.Tar;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// Independent review of T-F413: findings proven in process. Each test states the expected
// (correct) behavior, so a red test is a defect.
public sealed class ReviewBareGzipDefectTests : IDisposable
{
    private static readonly TarCapabilities Gzip = new() { SupportsGzip = true, Version = "bsdtar 3.8.9" };
    private readonly TempDirectory _temp = new();
    private readonly BareGzipRecordingTar _tar = new();

    public void Dispose() => _temp.Dispose();

    private string Parent => Path.Combine(_temp.Path, "parent");
    private string Destination => Path.Combine(Parent, "out");

    private string Write(string name, byte[] bytes)
    {
        string dir = Path.Combine(_temp.Path, "src");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private Task<ArchiveResult> ExtractAsync(string archive, ConflictBehavior onConflict) =>
        new ExtractionRouter(new NoZipServiceForReview(), _tar, Gzip, new GroupPolicyOptions()).ExtractAsync(
            new ExtractOptions { ArchivePaths = [archive], DestinationFolder = Destination, Mode = ExtractMode.SingleFolder, OnConflict = onConflict });

    [Fact]
    public void OutputName_DotsBeforeGz_IsNotDotDot() =>
        BareGzip.OutputName(@"C:\x\...gz").Should().NotBe("..");

    [Fact]
    public async Task Extract_ArchiveNamedDotDotDotGz_Rename_WritesNothingOutsideTheDestination()
    {
        Directory.CreateDirectory(Destination);
        string archive = Write("...gz", BareGzipTests.Member("PAYLOAD"u8.ToArray()));

        await ExtractAsync(archive, ConflictBehavior.Rename);

        Directory.GetFileSystemEntries(_temp.Path).Select(Path.GetFileName)
            .Should().BeEquivalentTo(["src", "parent"], "nothing may land beside the destination's parent");
    }

    [Fact]
    public async Task Extract_ArchiveNamedDotDotDotGz_RenameExisting_KeepsTheParentFolder()
    {
        Directory.CreateDirectory(Destination);
        File.WriteAllText(Path.Combine(Parent, "users-own-file.txt"), "keep");
        string archive = Write("...gz", BareGzipTests.Member("PAYLOAD"u8.ToArray()));

        ArchiveResult result = await ExtractAsync(archive, ConflictBehavior.RenameExisting);

        string seen = string.Join(" | ", Directory.GetFileSystemEntries(_temp.Path, "*", SearchOption.AllDirectories).Select(e => (File.Exists(e) ? "F " : "D ") + Path.GetRelativePath(_temp.Path, e)))
            + " errors=" + result.Errors.Count + " created=" + string.Join(",", result.CreatedFiles);
        File.Exists(Path.Combine(Parent, "users-own-file.txt")).Should().BeTrue("the folder above the destination must stay where it is; on disk: {0}", seen);
    }

    [Fact]
    public async Task Extract_ArchiveNamedSpaceGz_RenameExisting_KeepsTheDestinationFolder()
    {
        Directory.CreateDirectory(Destination);
        File.WriteAllText(Path.Combine(Destination, "users-own-file.txt"), "keep");
        string archive = Write(" .gz", BareGzipTests.Member("PAYLOAD"u8.ToArray()));

        await ExtractAsync(archive, ConflictBehavior.RenameExisting);

        File.Exists(Path.Combine(Destination, "users-own-file.txt")).Should().BeTrue("the destination folder must not be renamed away");
    }

    [Fact]
    public void Read_JunkBetweenDeflateEndAndTrailer_IsDamaged()
    {
        byte[] member = BareGzipTests.Member("hello"u8.ToArray());
        byte[] spliced = [.. member[..^8], .. "JUNKJUNK"u8.ToArray(), .. member[^8..]];
        string path = Write("j.gz", spliced);

        BareGzip.Read(path, null, CancellationToken.None).Damaged.Should().BeTrue("gzip -t reports a CRC error here");
    }

    [Fact]
    public void IsBareGzip_TarGzWhoseFirstMemberIsEmpty_StaysOnTheTarRoute()
    {
        using var tar = new MemoryStream();
        using (var writer = new TarWriter(tar, TarEntryFormat.Ustar, leaveOpen: true))
            writer.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, "in.txt") { DataStream = new MemoryStream("x"u8.ToArray()) });
        byte[] tarGz = [.. BareGzipTests.Member([]), .. BareGzipTests.Member(tar.ToArray())];
        string path = Write("x.tar.gz", tarGz);

        BareGzip.IsBareGzip(path).Should().BeFalse("bsdtar reads every member: this is a tar.gz");
    }
}

file sealed class NoZipServiceForReview : Archiver.Core.Interfaces.IArchiveService
{
    public Task<ArchiveResult> ArchiveAsync(ArchiveOptions options, IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException("not used");
    public Task<ArchiveResult> ExtractAsync(ExtractOptions options, IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(new ArchiveResult());
    public Task<ArchiveResult> TestAsync(IReadOnlyList<string> archivePaths, IProgress<ProgressReport>? progress = null, Func<PasswordPromptInfo, Task<PasswordDecision>>? resolvePasswordAsync = null, Func<string, bool, bool>? entryFilter = null, CancellationToken cancellationToken = default) => Task.FromResult(new ArchiveResult());
    public Task<ArchiveListResult> ListEntriesAsync(string archivePath, CancellationToken cancellationToken = default) => throw new InvalidOperationException("not used");
}
