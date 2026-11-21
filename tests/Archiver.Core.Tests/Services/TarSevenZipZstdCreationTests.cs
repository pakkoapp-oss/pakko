using System.Formats.Tar;
using System.IO.Compression;
using Archiver.Core.Interfaces;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

file sealed class WritesSevenZipOnlyTarService : ITarService
{
    public Task<ArchiveResult> TestAsync(IReadOnlyList<string> archivePaths, IProgress<ProgressReport>? progress = null, Func<string, bool, bool>? entryFilter = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<bool> CanCreateSevenZipAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

    public Task<TarCapabilities> DetectCapabilitiesAsync() => throw new NotSupportedException();

    public Task<ArchiveResult> ExtractAsync(ExtractOptions options, IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public async Task<ArchiveListSummary> ListEntriesAsync(string archivePath, Action<ArchiveEntryInfo> onEntry, CancellationToken cancellationToken = default) =>
        ArchiveListSummary.Replay(await ListEntriesAsync(archivePath, cancellationToken), onEntry);

    public Task<ArchiveListResult> ListEntriesAsync(string archivePath, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<ArchiveResult> CompressAsync(ArchiveOptions options, IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
}

/// <summary>
/// T-F565 (v1.12.0 batch 3): Zstandard as a 7z method, offered only where tar.exe was seen writing
/// it - libarchive takes the name zstd without its encoder (3.8.1's writer has a stub), so the probe
/// writes a real entry. Creation against the real tar.exe and 7-Zip: Archiver.Core.PerformanceTests.
/// </summary>
public sealed class TarSevenZipZstdCreationTests : IDisposable
{
    private static readonly TarCapabilities ReadsZstdIn7z =
        TarVersionParser.Parse("bsdtar 3.8.9 - libarchive 3.8.9 zlib/1.2.13.1-motley liblzma/5.8.3 bz2lib/1.0.8 libzstd/1.5.7");
    private static readonly TarCapabilities NoZstd =
        TarVersionParser.Parse("bsdtar 3.8.9 - libarchive 3.8.9 zlib/1.2.13.1-motley liblzma/5.8.3 bz2lib/1.0.8");

    private readonly TempDirectory _temp = new();
    private int _writerProbes;
    private int _zstdProbes;

    public void Dispose() => _temp.Dispose();

    // Each zstd question takes the next answer; the last one repeats.
    private TarSandboxedService Service(
        bool writes7z = true, TarCapabilities? caps = null, GroupPolicyOptions? policy = null, params bool?[] zstdAnswers) => new(
        policy ?? new GroupPolicyOptions(),
        () => Task.FromResult(caps ?? ReadsZstdIn7z),
        sevenZipWriterProbe: () => { _writerProbes++; return Task.FromResult<bool?>(writes7z); },
        sevenZipZstdWriterProbe: () =>
        {
            bool?[] answers = zstdAnswers.Length == 0 ? [true] : zstdAnswers;
            return Task.FromResult(answers[Math.Min(_zstdProbes++, answers.Length - 1)]);
        });

    private ArchiveOptions ZstdOptions()
    {
        string source = Path.Combine(_temp.Path, "a.txt");
        File.WriteAllText(source, "a");
        return new ArchiveOptions
        {
            SourcePaths = [source],
            DestinationFolder = Path.Combine(_temp.Path, "out"),
            ArchiveName = "out",
            Format = ArchiveContainerFormat.SevenZip,
            SevenZipMethod = SevenZipMethod.Zstd,
        };
    }

    private static List<string> Args(SevenZipMethod method, CompressionLevel level, CompressionThreads.Limit limit)
    {
        var args = new List<string>();
        TarSandboxedService.AppendSevenZipArgs(args, method, level, limit);
        return args;
    }

    private static readonly CompressionThreads.Limit TwelveCoresFourGiB = new(0, 12, 4L * 1024 * 1024 * 1024);

    // --- Happy path ---

    // zstd's own levels, as tar.zst takes them (libarchive hands compression-level to zstd as is).
    [Theory]
    [InlineData(CompressionLevel.Fastest, "7zip:compression=zstd,compression-level=1")]
    [InlineData(CompressionLevel.Optimal, "7zip:compression=zstd,compression-level=9")]
    [InlineData(CompressionLevel.SmallestSize, "7zip:compression=zstd,compression-level=19")]
    public void AppendSevenZipArgs_Zstd_TakesZstdsLevels(CompressionLevel level, string options) =>
        Args(SevenZipMethod.Zstd, level, CompressionThreads.Limit.OneThread).Should().Equal("--format=7zip", "--options", options);

    [Fact]
    public void AppendSevenZipArgs_Zstd_ThreadsAsTarZstHasThem() =>
        Args(SevenZipMethod.Zstd, CompressionLevel.SmallestSize, TwelveCoresFourGiB)
            .Should().Equal("--format=7zip", "--options", "7zip:compression=zstd,compression-level=19,7zip:threads=12");

    // libarchive's 7z writer has threads for zstd alone.
    [Theory]
    [InlineData(SevenZipMethod.Lzma2, "7zip:compression=lzma2,compression-level=9")]
    [InlineData(SevenZipMethod.Deflate, "7zip:compression=deflate,compression-level=9")]
    public void AppendSevenZipArgs_OtherMethods_NoThreads(SevenZipMethod method, string options) =>
        Args(method, CompressionLevel.SmallestSize, TwelveCoresFourGiB).Should().Equal("--format=7zip", "--options", options);

    [Fact]
    public void AppendSevenZipArgs_ZstdStored_IsCopy() =>
        Args(SevenZipMethod.Zstd, CompressionLevel.NoCompression, TwelveCoresFourGiB)
            .Should().Equal("--format=7zip", "--options", "7zip:compression=copy");

    [Fact]
    public async Task CanCreateSevenZipZstdAsync_Writes_IsTrueAndAskedOnce()
    {
        TarSandboxedService sut = Service();

        (await sut.CanCreateSevenZipZstdAsync()).Should().BeTrue();
        (await sut.CanCreateSevenZipZstdAsync()).Should().BeTrue();

        _zstdProbes.Should().Be(1);
    }

    [Fact]
    public void ZstdProbeInput_IsOneSmallFileInATar_BuiltInMemory()
    {
        using var input = new MemoryStream(TarSandboxedService.SevenZipZstdProbeInput());
        using var reader = new TarReader(input);

        TarEntry entry = reader.GetNextEntry()!;
        entry.EntryType.Should().Be(TarEntryType.RegularFile);
        entry.Length.Should().BePositive();
        reader.GetNextEntry().Should().BeNull();
    }

    // --- Security & Boundary ---

    [Fact]
    public async Task CanCreateSevenZipZstdAsync_TarDisabledByPolicy_IsFalseAndStartsNothing()
    {
        TarSandboxedService sut = Service(policy: new GroupPolicyOptions { DisableTarExtraction = true });

        (await sut.CanCreateSevenZipZstdAsync()).Should().BeFalse();

        (_writerProbes, _zstdProbes).Should().Be((0, 0));
    }

    [Fact]
    public async Task CanCreateSevenZipZstdAsync_NoSevenZipWriter_IsFalseAndZstdIsNotAsked()
    {
        TarSandboxedService sut = Service(writes7z: false);

        (await sut.CanCreateSevenZipZstdAsync()).Should().BeFalse();

        _zstdProbes.Should().Be(0);
    }

    // A zstd 7z Pakko could not open again is not offered.
    [Fact]
    public async Task CanCreateSevenZipZstdAsync_TarWithoutZstd_IsFalseAndZstdIsNotAsked()
    {
        TarSandboxedService sut = Service(caps: NoZstd);

        (await sut.CanCreateSevenZipZstdAsync()).Should().BeFalse();

        _zstdProbes.Should().Be(0);
    }

    // --- Misuse ---

    [Fact]
    public async Task ITarService_CanCreateSevenZipZstdAsync_DefaultsToNo()
    {
        ITarService engine = new WritesSevenZipOnlyTarService();

        (await engine.CanCreateSevenZipZstdAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task CompressAsync_ZstdTarCannotWrite_IsRefusedNamingTheMethodAndWritesNothing()
    {
        TarSandboxedService sut = Service(zstdAnswers: [false]);

        ArchiveResult result = await sut.CompressAsync(ZstdOptions());

        CoreText text = result.Errors.Should().ContainSingle().Which.Text!;
        text.Code.Should().Be(MessageCode.FormatNotSupportedByTar);
        text.English.Should().StartWith("7-Zip Zstandard is not supported by this system's tar.exe");
        result.CreatedFiles.Should().BeEmpty();
        Directory.Exists(Path.Combine(_temp.Path, "out")).Should().BeFalse();
    }

    [Fact]
    public async Task CompressAsync_OtherMethod_DoesNotAskAboutZstd()
    {
        TarSandboxedService sut = Service(writes7z: false);

        await sut.CompressAsync(ZstdOptions() with { SevenZipMethod = SevenZipMethod.Lzma2 });

        _zstdProbes.Should().Be(0);
    }

    // --- Error path: no answer is not "no" ---

    [Fact]
    public async Task CanCreateSevenZipZstdAsync_NoAnswer_IsFalseAfterOneRetry_AndIsNotKept()
    {
        TarSandboxedService sut = Service(zstdAnswers: [null, null, true]);

        (await sut.CanCreateSevenZipZstdAsync()).Should().BeFalse();
        _zstdProbes.Should().Be(2);

        (await sut.CanCreateSevenZipZstdAsync()).Should().BeTrue();
        (await sut.CanCreateSevenZipZstdAsync()).Should().BeTrue();
        _zstdProbes.Should().Be(3);
    }

    [Fact]
    public async Task CanCreateSevenZipZstdAsync_AnsweredNo_IsKept()
    {
        TarSandboxedService sut = Service(zstdAnswers: [false, true]);

        (await sut.CanCreateSevenZipZstdAsync()).Should().BeFalse();
        (await sut.CanCreateSevenZipZstdAsync()).Should().BeFalse();

        _zstdProbes.Should().Be(1);
    }

    [Fact]
    public async Task CanCreateSevenZipZstdAsync_ConcurrentQuestions_ShareOneProbe()
    {
        var pending = new TaskCompletionSource<bool?>();
        var sut = new TarSandboxedService(
            new GroupPolicyOptions(), () => Task.FromResult(ReadsZstdIn7z),
            sevenZipWriterProbe: () => Task.FromResult<bool?>(true),
            sevenZipZstdWriterProbe: () => { _zstdProbes++; return pending.Task; });

        Task<bool> first = sut.CanCreateSevenZipZstdAsync();
        Task<bool> second = sut.CanCreateSevenZipZstdAsync();
        pending.SetResult(true);

        (await first).Should().BeTrue();
        (await second).Should().BeTrue();
        _zstdProbes.Should().Be(1);
    }

    [Fact]
    public async Task CanCreateSevenZipZstdAsync_CancelledWhileTheProbeRuns_Throws()
    {
        var pending = new TaskCompletionSource<bool?>();
        var sut = new TarSandboxedService(
            new GroupPolicyOptions(), () => Task.FromResult(ReadsZstdIn7z),
            sevenZipWriterProbe: () => Task.FromResult<bool?>(true),
            sevenZipZstdWriterProbe: () => pending.Task);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Func<Task> act = () => sut.CanCreateSevenZipZstdAsync(cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
