using Archiver.Core.Interfaces;
using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// Hand-rolled fakes — no mocking library is used anywhere in this repo, mirroring
// ExtractionRouterTests.cs's/ArchiveListingRouterTests.cs's exact pattern for the sibling
// routers.
file sealed class FakeArchiveService : IArchiveService
{
    public ArchiveOptions? LastArchiveOptions;
    public int ArchiveCallCount;
    public ArchiveResult ArchiveResult = new() { Success = true };

    public Task<ArchiveResult> ArchiveAsync(ArchiveOptions options, IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default)
    {
        ArchiveCallCount++;
        LastArchiveOptions = options;
        return Task.FromResult(ArchiveResult);
    }

    public Task<ArchiveResult> ExtractAsync(ExtractOptions options, IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<ArchiveResult> TestAsync(IReadOnlyList<string> archivePaths, IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<ArchiveListResult> ListEntriesAsync(string archivePath, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}

file sealed class FakeTarService : ITarService
{
    public ArchiveOptions? LastCompressOptions;
    public int CompressCallCount;
    public ArchiveResult CompressResult = new() { Success = true };

    public Task<TarCapabilities> DetectCapabilitiesAsync() => Task.FromResult(new TarCapabilities());

    public Task<ArchiveResult> ExtractAsync(ExtractOptions options, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<ArchiveListResult> ListEntriesAsync(string archivePath, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<ArchiveResult> CompressAsync(ArchiveOptions options, IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default)
    {
        CompressCallCount++;
        LastCompressOptions = options;
        return Task.FromResult(CompressResult);
    }
}

public sealed class ArchiveCreationRouterTests
{
    [Fact]
    public async Task ArchiveAsync_ZipFormat_RoutesToArchiveService()
    {
        var zipService = new FakeArchiveService();
        var tarService = new FakeTarService();
        var router = new ArchiveCreationRouter(zipService, tarService);
        var options = new ArchiveOptions { SourcePaths = ["a.txt"], Format = ArchiveContainerFormat.Zip };

        var result = await router.ArchiveAsync(options);

        result.Should().Be(zipService.ArchiveResult);
        zipService.ArchiveCallCount.Should().Be(1);
        tarService.CompressCallCount.Should().Be(0);
        zipService.LastArchiveOptions.Should().Be(options);
    }

    [Theory]
    [InlineData(ArchiveContainerFormat.Tar)]
    [InlineData(ArchiveContainerFormat.TarGz)]
    [InlineData(ArchiveContainerFormat.TarBz2)]
    [InlineData(ArchiveContainerFormat.TarXz)]
    [InlineData(ArchiveContainerFormat.TarZst)]
    [InlineData(ArchiveContainerFormat.TarLzma)]
    public async Task ArchiveAsync_TarFamilyFormat_RoutesToTarService(ArchiveContainerFormat format)
    {
        var zipService = new FakeArchiveService();
        var tarService = new FakeTarService();
        var router = new ArchiveCreationRouter(zipService, tarService);
        var options = new ArchiveOptions { SourcePaths = ["a.txt"], Format = format };

        var result = await router.ArchiveAsync(options);

        result.Should().Be(tarService.CompressResult);
        tarService.CompressCallCount.Should().Be(1);
        zipService.ArchiveCallCount.Should().Be(0);
        tarService.LastCompressOptions.Should().Be(options);
    }
}
