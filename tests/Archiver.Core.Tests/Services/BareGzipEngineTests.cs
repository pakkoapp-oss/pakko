using System.Formats.Tar;
using Archiver.Core.Interfaces;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// The tar route: records what reached it, writes nothing, never starts tar.exe.
internal sealed class BareGzipRecordingTar : ITarService
{
    public List<string> Extracted { get; } = [];
    public List<string> Tested { get; } = [];
    public List<string> Listed { get; } = [];

    public Task<bool> CanCreateSevenZipAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

    public Task<TarCapabilities> DetectCapabilitiesAsync() => Task.FromResult(new TarCapabilities { SupportsGzip = true });

    public Task<ArchiveResult> ExtractAsync(ExtractOptions options, IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default)
    {
        Extracted.AddRange(options.ArchivePaths);
        return Task.FromResult(new ArchiveResult());
    }

    public Task<ArchiveResult> TestAsync(IReadOnlyList<string> archivePaths, IProgress<ProgressReport>? progress = null, Func<string, bool, bool>? entryFilter = null, CancellationToken cancellationToken = default)
    {
        Tested.AddRange(archivePaths);
        return Task.FromResult(new ArchiveResult());
    }

    public Task<ArchiveListResult> ListEntriesAsync(string archivePath, CancellationToken cancellationToken = default)
    {
        Listed.Add(archivePath);
        return Task.FromResult(new ArchiveListResult { Success = true });
    }

    public Task<ArchiveResult> CompressAsync(ArchiveOptions options, IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("not a creation test");
}

file sealed class NoZipService : IArchiveService
{
    public Task<ArchiveResult> ArchiveAsync(ArchiveOptions options, IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("the ZIP engine must not run");

    public Task<ArchiveResult> ExtractAsync(ExtractOptions options, IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default)
        => Task.FromResult(new ArchiveResult());

    public Task<ArchiveResult> TestAsync(IReadOnlyList<string> archivePaths, IProgress<ProgressReport>? progress = null, Func<PasswordPromptInfo, Task<PasswordDecision>>? resolvePasswordAsync = null, Func<string, bool, bool>? entryFilter = null, CancellationToken cancellationToken = default)
        => Task.FromResult(new ArchiveResult());

    public Task<ArchiveListResult> ListEntriesAsync(string archivePath, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("the ZIP engine must not run");
}

/// <summary>
/// T-F413: x, t and l of a gzip of one file through the routers - in process, tar.exe never
/// starts (a tar.gz goes to a recording fake). The engine keeps every rule the other two keep.
/// </summary>
public sealed class BareGzipEngineTests : IDisposable
{
    private static readonly TarCapabilities Gzip = new() { SupportsGzip = true, Version = "bsdtar 3.8.9" };
    private const uint Mtime = 1_700_000_000;

    private readonly TempDirectory _temp = new();
    private readonly BareGzipRecordingTar _tar = new();

    public void Dispose() => _temp.Dispose();

    private string Destination => Path.Combine(_temp.Path, "out");

    private string Gz(string name, string text, uint mtime = Mtime) => Gz(name, System.Text.Encoding.UTF8.GetBytes(text), mtime);

    private string Gz(string name, byte[] data, uint mtime = Mtime)
    {
        string path = Path.Combine(_temp.Path, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, BareGzipTests.Member(data, mtime: mtime));
        return path;
    }

    // One byte over and over: compresses past 1000:1. Not zeros, which start like an empty tar.
    private static byte[] Same(int length)
    {
        byte[] data = new byte[length];
        Array.Fill(data, (byte)'A');
        return data;
    }

    private ExtractionRouter Router(GroupPolicyOptions? policy = null) => new(new NoZipService(), _tar, Gzip, policy ?? new GroupPolicyOptions());

    private Task<ArchiveResult> ExtractAsync(ExtractOptions options, GroupPolicyOptions? policy = null) =>
        Router(policy).ExtractAsync(options with { DestinationFolder = Destination, Mode = ExtractMode.SingleFolder });

    private string[] Files() =>
        Directory.Exists(Destination)
            ? [.. Directory.GetFileSystemEntries(Destination).Select(Path.GetFileName).Order(StringComparer.Ordinal)!]
            : [];

    private string Read(string name) => File.ReadAllText(Path.Combine(Destination, name));

    // --- Happy path ---

    [Fact]
    public async Task Extract_OneFile_LandsUnderTheArchivesNameWithoutGz()
    {
        string archive = Gz("notes.txt.gz", "hello");
        var extracted = new List<string>();

        ArchiveResult result = await ExtractAsync(new ExtractOptions { ArchivePaths = [archive], OnItemExtracted = extracted.Add });

        result.Errors.Should().BeEmpty();
        Files().Should().Equal("notes.txt");
        Read("notes.txt").Should().Be("hello");
        File.GetLastWriteTimeUtc(Path.Combine(Destination, "notes.txt")).Should().Be(DateTime.UnixEpoch.AddSeconds(Mtime));
        result.CreatedFiles.Should().Equal(Destination);
        result.Sources.Should().ContainSingle().Which.Outcome.Should().Be(SourceOutcome.Completed);
        extracted.Should().Equal(Path.Combine(Destination, "notes.txt"));
        _tar.Extracted.Should().BeEmpty();
    }

    [Fact]
    public async Task Extract_TarGzAndBareGz_EachGoesItsOwnWay()
    {
        using var tar = new MemoryStream();
        using (var writer = new TarWriter(tar, TarEntryFormat.Ustar, leaveOpen: true))
            writer.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, "in.txt") { DataStream = new MemoryStream("x"u8.ToArray()) });
        string tarGz = Gz("x.tar.gz", tar.ToArray());
        string bare = Gz("a.txt.gz", "a");

        ArchiveResult result = await ExtractAsync(new ExtractOptions { ArchivePaths = [tarGz, bare] });

        _tar.Extracted.Should().Equal(tarGz);
        Files().Should().Equal("a.txt");
        result.Sources.Should().ContainSingle(s => s.Path == bare);
    }

    // A tgz that holds no tar is a gzip of something else; 7-Zip names it x.tar too.
    [Fact]
    public async Task Extract_Tgz_IsNamedDotTar()
    {
        await ExtractAsync(new ExtractOptions { ArchivePaths = [Gz("x.tgz", "not a tar")] });

        Files().Should().Equal("x.tar");
    }

    [Fact]
    public async Task Extract_SeparateFolders_LandsInTheDestinationItself()
    {
        string archive = Gz("a.txt.gz", "a");

        await Router().ExtractAsync(new ExtractOptions { ArchivePaths = [archive], DestinationFolder = Destination, Mode = ExtractMode.SeparateFolders });

        Files().Should().Equal("a.txt");
    }

    // T-F45: the archive's download mark goes to the file.
    [Fact]
    public async Task Extract_DownloadedArchive_MarksTheFile()
    {
        string archive = Gz("a.txt.gz", "a");
        File.WriteAllText(archive + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");

        await ExtractAsync(new ExtractOptions { ArchivePaths = [archive] });

        File.ReadAllText(Path.Combine(Destination, "a.txt") + ":Zone.Identifier").Should().Contain("ZoneId=3");
        File.GetLastWriteTimeUtc(Path.Combine(Destination, "a.txt")).Should().Be(DateTime.UnixEpoch.AddSeconds(Mtime), "the mark keeps the time");
    }

    [Fact]
    public async Task Extract_DownloadMarkOff_LeavesItOff()
    {
        string archive = Gz("a.txt.gz", "a");
        File.WriteAllText(archive + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");

        await ExtractAsync(new ExtractOptions { ArchivePaths = [archive], ApplyDownloadMark = false });

        File.Exists(Path.Combine(Destination, "a.txt") + ":Zone.Identifier").Should().BeFalse();
    }

    // --- Conflicts: the shared rules ---

    private string Existing(string name = "a.txt", string text = "OLD")
    {
        Directory.CreateDirectory(Destination);
        string path = Path.Combine(Destination, name);
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public async Task Extract_ExistingFile_Skip_KeepsItAndSaysSo()
    {
        Existing();

        ArchiveResult result = await ExtractAsync(new ExtractOptions { ArchivePaths = [Gz("a.txt.gz", "NEW")], OnConflict = ConflictBehavior.Skip });

        Read("a.txt").Should().Be("OLD");
        result.SkippedFiles.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.FileExistsAtDestination);
        result.CreatedFiles.Should().BeEmpty();
        result.Sources.Should().ContainSingle().Which.Outcome.Should().Be(SourceOutcome.NotProcessed);
        Files().Should().Equal("a.txt");
    }

    [Fact]
    public async Task Extract_ExistingFile_Overwrite_ReplacesIt()
    {
        Existing();

        ArchiveResult result = await ExtractAsync(new ExtractOptions { ArchivePaths = [Gz("a.txt.gz", "NEW")], OnConflict = ConflictBehavior.Overwrite });

        result.Errors.Should().BeEmpty();
        Read("a.txt").Should().Be("NEW");
        Files().Should().Equal("a.txt");
    }

    [Fact]
    public async Task Extract_ExistingFile_Rename_NumbersTheNewOne()
    {
        Existing();

        await ExtractAsync(new ExtractOptions { ArchivePaths = [Gz("a.txt.gz", "NEW")], OnConflict = ConflictBehavior.Rename });

        Read("a.txt").Should().Be("OLD");
        Read("a (1).txt").Should().Be("NEW");
    }

    [Fact]
    public async Task Extract_ExistingFile_RenameExisting_NumbersTheOldOne()
    {
        Existing();

        await ExtractAsync(new ExtractOptions { ArchivePaths = [Gz("a.txt.gz", "NEW")], OnConflict = ConflictBehavior.RenameExisting });

        Read("a.txt").Should().Be("NEW");
        Read("a (1).txt").Should().Be("OLD");
    }

    [Fact]
    public async Task Extract_FolderInTheWay_IsAnErrorAndKeepsIt()
    {
        Directory.CreateDirectory(Path.Combine(Destination, "a.txt"));

        ArchiveResult result = await ExtractAsync(new ExtractOptions { ArchivePaths = [Gz("a.txt.gz", "NEW")], OnConflict = ConflictBehavior.Overwrite });

        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.DestinationIsAFolder);
        Directory.Exists(Path.Combine(Destination, "a.txt")).Should().BeTrue();
    }

    // T-F415: two archives of one name into one folder - the second meets this run's file.
    [Fact]
    public async Task Extract_TwoArchivesOfOneName_Skip_TheSecondIsThisRunsTwin()
    {
        string first = Gz(Path.Combine("d1", "a.txt.gz"), "ONE");
        string second = Gz(Path.Combine("d2", "a.txt.gz"), "TWO");

        ArchiveResult result = await ExtractAsync(new ExtractOptions { ArchivePaths = [first, second], OnConflict = ConflictBehavior.Skip });

        Read("a.txt").Should().Be("ONE");
        result.SkippedFiles.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.FileWrittenEarlierInThisRun);
    }

    // T-F420: the first archive's file was replaced, so that archive is not wholly on disk.
    [Fact]
    public async Task Extract_TwoArchivesOfOneName_Overwrite_TheFirstIsNotDeletable()
    {
        string first = Gz(Path.Combine("d1", "a.txt.gz"), "ONE");
        string second = Gz(Path.Combine("d2", "a.txt.gz"), "TWO");

        ArchiveResult result = await ExtractAsync(new ExtractOptions { ArchivePaths = [first, second], OnConflict = ConflictBehavior.Overwrite });

        Read("a.txt").Should().Be("TWO");
        result.Sources.Single(s => s.Path == first).Outcome.Should().Be(SourceOutcome.Partial);
        result.Sources.Single(s => s.Path == second).Outcome.Should().Be(SourceOutcome.Completed);
    }

    [Fact]
    public async Task Extract_ExistingFile_Ask_UsesTheCallback()
    {
        Existing();
        var asked = new List<string>();

        await ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [Gz("a.txt.gz", "NEW")],
            OnConflict = ConflictBehavior.Ask,
            ResolveConflictAsync = info => { asked.Add(info.ExistingPath); return Task.FromResult(new ConflictDecision { Resolution = ConflictResolution.Overwrite }); },
        });

        asked.Should().Equal(Path.Combine(Destination, "a.txt"));
        Read("a.txt").Should().Be("NEW");
    }

    // --- Misuse: selection and name filters ---

    [Fact]
    public async Task Extract_FilterLeavesTheFileOut_WritesNothing()
    {
        var seen = new List<(string, bool)>();

        ArchiveResult result = await ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [Gz("a.txt.gz", "a")],
            EntryFilter = (name, isFolder) => { seen.Add((name, isFolder)); return false; },
        });

        seen.Should().Equal(("a.txt", false));
        Files().Should().BeEmpty();
        result.Errors.Should().BeEmpty();
        result.SkippedFiles.Should().BeEmpty();
        Directory.Exists(Destination).Should().BeFalse("T-F309: the folder this run made goes again");
    }

    [Fact]
    public async Task Extract_FilterTakesTheFile_IsNotDeletable()
    {
        ArchiveResult result = await ExtractAsync(new ExtractOptions { ArchivePaths = [Gz("a.txt.gz", "a")], EntryFilter = (_, _) => true });

        Files().Should().Equal("a.txt");
        result.Sources.Should().ContainSingle().Which.Outcome.Should().Be(SourceOutcome.Partial);
    }

    [Fact]
    public async Task Extract_SelectionWithoutTheName_WritesNothing()
    {
        await ExtractAsync(new ExtractOptions { ArchivePaths = [Gz("a.txt.gz", "a")], SelectedEntryPaths = ["b.txt"] });

        Files().Should().BeEmpty();
    }

    // --- Security & Boundary ---

    [Fact]
    public async Task Extract_TarDisabledByPolicy_IsRefusedLikeATarGz()
    {
        ArchiveResult result = await ExtractAsync(new ExtractOptions { ArchivePaths = [Gz("a.txt.gz", "a")] }, new GroupPolicyOptions { DisableTarExtraction = true });

        result.SkippedFiles.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.TarExtractionDisabled);
        Files().Should().BeEmpty();
    }

    [Fact]
    public async Task Extract_GzipBlockedByPolicy_IsRefused()
    {
        ArchiveResult result = await ExtractAsync(new ExtractOptions { ArchivePaths = [Gz("a.txt.gz", "a")] }, new GroupPolicyOptions { BlockedFormats = ["gzip"] });

        result.SkippedFiles.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.FormatBlocked);
        Files().Should().BeEmpty();
    }

    // T-F94: zeros compress past 1000:1; nothing confirms, so nothing is written.
    [Fact]
    public async Task Extract_CompressionBomb_NotConfirmed_WritesNothing()
    {
        string archive = Gz("zeros.bin.gz", Same(40 * 1024 * 1024));

        ArchiveResult result = await ExtractAsync(new ExtractOptions { ArchivePaths = [archive] });

        result.SkippedFiles.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.TarBombDeclined);
        Files().Should().BeEmpty();
    }

    [Fact]
    public async Task Extract_CompressionBomb_Confirmed_IsWritten()
    {
        string archive = Gz("zeros.bin.gz", Same(40 * 1024 * 1024));
        CompressionBombWarning? warning = null;

        await ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archive],
            ConfirmCompressionBombExtraction = w => { warning = w; return Task.FromResult(true); },
        });

        warning!.DeclaredUncompressedSize.Should().Be(40 * 1024 * 1024);
        new FileInfo(Path.Combine(Destination, "zeros.bin")).Length.Should().Be(40 * 1024 * 1024);
    }

    // --- Error path ---

    [Fact]
    public async Task Extract_Damaged_IsAnErrorAndLeavesNothing()
    {
        string archive = Gz("a.txt.gz", "hello world, long enough");
        byte[] bytes = File.ReadAllBytes(archive);
        bytes[^8] ^= 0xFF; // the CRC
        File.WriteAllBytes(archive, bytes);
        Existing("other.txt", "keep");

        ArchiveResult result = await ExtractAsync(new ExtractOptions { ArchivePaths = [archive] });

        CoreText text = result.Errors.Should().ContainSingle().Which.Text!;
        text.English.Should().Be("Cannot extract archive: The compressed data is damaged.");
        Files().Should().Equal("other.txt");
        result.Sources.Should().ContainSingle().Which.Outcome.Should().Be(SourceOutcome.NotProcessed);
    }

    [Fact]
    public async Task Extract_Cancelled_ThrowsAndLeavesNothing()
    {
        string archive = Gz("big.bin.gz", Same(8 * 1024 * 1024));
        Existing("other.txt", "keep");
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        Func<Task> run = () => Router().ExtractAsync(
            new ExtractOptions { ArchivePaths = [archive], DestinationFolder = Destination, Mode = ExtractMode.SingleFolder }, null, cancel.Token);

        await run.Should().ThrowAsync<OperationCanceledException>();
        Files().Should().Equal("other.txt");
    }

    // --- t ---

    [Fact]
    public async Task Test_Intact_Passes()
    {
        string archive = Gz("a.txt.gz", "a");

        ArchiveResult result = await Router().TestAsync([archive]);

        result.Errors.Should().BeEmpty();
        result.Warnings.Should().BeEmpty("every byte was checked, not only a structure");
        result.Sources.Should().ContainSingle().Which.Outcome.Should().Be(SourceOutcome.Completed);
        _tar.Tested.Should().BeEmpty();
    }

    [Fact]
    public async Task Test_CutShort_Fails()
    {
        string archive = Gz("a.txt.gz", "hello world, long enough");
        File.WriteAllBytes(archive, File.ReadAllBytes(archive)[..^4]);

        ArchiveResult result = await Router().TestAsync([archive]);

        result.Errors.Should().ContainSingle().Which.Text!.English.Should().Be("Cannot read archive: The compressed data is damaged.");
        result.Sources.Should().ContainSingle().Which.Outcome.Should().Be(SourceOutcome.Partial);
    }

    [Fact]
    public async Task Test_FilterLeavesTheFileOut_ReadsNothing()
    {
        ArchiveResult result = await Router().TestAsync([Gz("a.txt.gz", "a")], entryFilter: (_, _) => false);

        result.Errors.Should().BeEmpty();
        result.Sources.Should().BeEmpty();
    }

    // --- l ---

    [Fact]
    public async Task List_IsTheOneFile()
    {
        string archive = Gz("a.txt.gz", "hello");
        var router = new ArchiveListingRouter(new NoZipService(), _tar, Gzip, new GroupPolicyOptions());

        ArchiveListResult result = await router.ListEntriesAsync(archive);

        result.Success.Should().BeTrue();
        ArchiveEntryInfo entry = result.Entries.Should().ContainSingle().Subject;
        entry.Path.Should().Be("a.txt");
        entry.Size.Should().Be(5);
        entry.CompressedSize.Should().Be(new FileInfo(archive).Length);
        entry.Modified.Should().Be(DateTime.UnixEpoch.AddSeconds(Mtime).ToLocalTime());
        entry.IsDirectory.Should().BeFalse();
        _tar.Listed.Should().BeEmpty();
    }

    [Fact]
    public async Task List_Damaged_Fails()
    {
        string archive = Gz("a.txt.gz", "hello world, long enough");
        File.WriteAllBytes(archive, [.. File.ReadAllBytes(archive), .. "junk"u8.ToArray()]);
        var router = new ArchiveListingRouter(new NoZipService(), _tar, Gzip, new GroupPolicyOptions());

        ArchiveListResult result = await router.ListEntriesAsync(archive);

        result.Success.Should().BeFalse();
        result.ErrorText!.English.Should().Be("Cannot read archive: The compressed data is damaged.");
    }
}
