using Archiver.Core.Interfaces;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// Hand-rolled fakes — no mocking library is used anywhere in this repo (matches existing
// convention). Each fake records the ExtractOptions it was called with and returns a
// caller-supplied ArchiveResult, so tests can assert both "was this called" and "with what".
file sealed class FakeArchiveService : IArchiveService
{
    public ExtractOptions? LastExtractOptions;
    public int ExtractCallCount;
    public ArchiveResult ExtractResult = new() { Success = true };

    public Task<ArchiveResult> ArchiveAsync(ArchiveOptions options, IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<ArchiveResult> ExtractAsync(ExtractOptions options, IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default)
    {
        ExtractCallCount++;
        LastExtractOptions = options;
        return Task.FromResult(ExtractResult);
    }

    public Task<ArchiveResult> TestAsync(IReadOnlyList<string> archivePaths, IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<ArchiveListResult> ListEntriesAsync(string archivePath, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}

file sealed class FakeTarService : ITarService
{
    public ExtractOptions? LastExtractOptions;
    public int ExtractCallCount;
    public ArchiveResult ExtractResult = new() { Success = true };

    public Task<TarCapabilities> DetectCapabilitiesAsync() => Task.FromResult(new TarCapabilities());

    public Task<ArchiveResult> ExtractAsync(ExtractOptions options, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        ExtractCallCount++;
        LastExtractOptions = options;
        return Task.FromResult(ExtractResult);
    }

    public Task<ArchiveListResult> ListEntriesAsync(string archivePath, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<ArchiveResult> CompressAsync(ArchiveOptions options, IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}

public sealed class ExtractionRouterTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string WriteBytes(string name, byte[] bytes)
    {
        var path = Path.Combine(_temp.Path, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private string WriteZip(string name) => WriteBytes(name, [0x50, 0x4B, 0x03, 0x04, 0, 0, 0, 0]);

    private string WriteTar(string name)
    {
        var header = new byte[512];
        var ustar = System.Text.Encoding.ASCII.GetBytes("ustar");
        Array.Copy(ustar, 0, header, 257, ustar.Length);
        return WriteBytes(name, header);
    }

    private string WriteRar(string name) => WriteBytes(name, [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00]);

    private static readonly TarCapabilities AllSupported = new()
    {
        SupportsRar = true,
        Supports7z = true,
        SupportsZstd = true,
        SupportsXz = true,
        SupportsLzma = true,
        SupportsBz2 = true,
        Version = "bsdtar 3.8.4",
    };

    [Fact]
    public async Task ExtractAsync_PureZipSelection_OnlyCallsZipService()
    {
        var zip = WriteBytes("only.zip", [0x50, 0x4B, 0x03, 0x04, 0, 0, 0, 0]);
        var zipService = new FakeArchiveService();
        var tarService = new FakeTarService();
        var router = new ExtractionRouter(zipService, tarService, AllSupported);

        var result = await router.ExtractAsync(new ExtractOptions { ArchivePaths = [zip], DestinationFolder = _temp.Path });

        zipService.ExtractCallCount.Should().Be(1);
        tarService.ExtractCallCount.Should().Be(0);
        zipService.LastExtractOptions!.ArchivePaths.Should().BeEquivalentTo([zip]);
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ExtractAsync_PureTarSelection_OnlyCallsTarService()
    {
        var tar = WriteTar("only.tar");
        var zipService = new FakeArchiveService();
        var tarService = new FakeTarService();
        var router = new ExtractionRouter(zipService, tarService, AllSupported);

        var result = await router.ExtractAsync(new ExtractOptions { ArchivePaths = [tar], DestinationFolder = _temp.Path });

        tarService.ExtractCallCount.Should().Be(1);
        zipService.ExtractCallCount.Should().Be(0);
        tarService.LastExtractOptions!.ArchivePaths.Should().BeEquivalentTo([tar]);
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ExtractAsync_MixedSelection_CallsBothAndMergesResults()
    {
        var zip = WriteZip("a.zip");
        var tar = WriteTar("b.tar");

        var zipService = new FakeArchiveService
        {
            ExtractResult = new ArchiveResult
            {
                Success = true,
                CreatedFiles = ["zip-out"],
                Errors = [new ArchiveError { SourcePath = zip, Message = "zip error" }],
            }
        };
        var tarService = new FakeTarService
        {
            ExtractResult = new ArchiveResult
            {
                Success = true,
                CreatedFiles = ["tar-out"],
                SkippedFiles = [new SkippedFile { Path = tar, Reason = "tar skip" }],
            }
        };
        var router = new ExtractionRouter(zipService, tarService, AllSupported);

        var result = await router.ExtractAsync(new ExtractOptions { ArchivePaths = [zip, tar], DestinationFolder = _temp.Path });

        zipService.ExtractCallCount.Should().Be(1);
        tarService.ExtractCallCount.Should().Be(1);
        zipService.LastExtractOptions!.ArchivePaths.Should().BeEquivalentTo([zip]);
        tarService.LastExtractOptions!.ArchivePaths.Should().BeEquivalentTo([tar]);
        result.CreatedFiles.Should().BeEquivalentTo(["zip-out", "tar-out"]);
        result.Errors.Should().HaveCount(1);
        result.SkippedFiles.Should().HaveCount(1);
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ExtractAsync_RarUnsupportedByCapabilities_SkipsWithoutCallingEitherService()
    {
        var rar = WriteRar("only.rar");
        var zipService = new FakeArchiveService();
        var tarService = new FakeTarService();
        var noRar = AllSupported with { SupportsRar = false };
        var router = new ExtractionRouter(zipService, tarService, noRar);

        var result = await router.ExtractAsync(new ExtractOptions { ArchivePaths = [rar], DestinationFolder = _temp.Path });

        zipService.ExtractCallCount.Should().Be(0);
        tarService.ExtractCallCount.Should().Be(0);
        result.SkippedFiles.Should().ContainSingle(s => s.Path == rar && s.Reason.Contains("RAR"));
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ExtractAsync_MixedSelection_SubOptionsNeverRequestOpenDestinationFolder()
    {
        // Deliberately leaves the top-level OpenDestinationFolder at its default (false) — the
        // router's own end-of-method Process.Start("explorer.exe", ...) is not under test here
        // (it would spawn a real window); this test only asserts the sub-options handed to each
        // service always force OpenDestinationFolder=false, which the code does unconditionally
        // regardless of the top-level value.
        var zip = WriteZip("a.zip");
        var tar = WriteTar("b.tar");
        var zipService = new FakeArchiveService();
        var tarService = new FakeTarService();
        var router = new ExtractionRouter(zipService, tarService, AllSupported);

        await router.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [zip, tar],
            DestinationFolder = _temp.Path,
        });

        zipService.LastExtractOptions!.OpenDestinationFolder.Should().BeFalse();
        tarService.LastExtractOptions!.OpenDestinationFolder.Should().BeFalse();
    }
}
