using Archiver.Core.Interfaces;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// Hand-rolled fakes — no mocking library is used anywhere in this repo, mirroring
// ExtractionRouterTests.cs's exact pattern for the sibling extraction router.
file sealed class FakeArchiveService : IArchiveService
{
    public string? LastListedPath;
    public int ListCallCount;
    public ArchiveListResult ListResult = new() { Success = true };

    public Task<ArchiveResult> ArchiveAsync(ArchiveOptions options, IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<ArchiveResult> ExtractAsync(ExtractOptions options, IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<ArchiveResult> TestAsync(IReadOnlyList<string> archivePaths, IProgress<ProgressReport>? progress = null, Func<PasswordPromptInfo, Task<PasswordDecision>>? resolvePasswordAsync = null, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<ArchiveListResult> ListEntriesAsync(string archivePath, CancellationToken cancellationToken = default)
    {
        ListCallCount++;
        LastListedPath = archivePath;
        return Task.FromResult(ListResult);
    }
}

file sealed class FakeTarService : ITarService
{
    public string? LastListedPath;
    public int ListCallCount;
    public ArchiveListResult ListResult = new() { Success = true };

    public Task<TarCapabilities> DetectCapabilitiesAsync() => Task.FromResult(new TarCapabilities());

    public Task<ArchiveResult> ExtractAsync(ExtractOptions options, IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<ArchiveListResult> ListEntriesAsync(string archivePath, CancellationToken cancellationToken = default)
    {
        ListCallCount++;
        LastListedPath = archivePath;
        return Task.FromResult(ListResult);
    }

    public Task<ArchiveResult> CompressAsync(ArchiveOptions options, IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}

public sealed class ArchiveListingRouterTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string WriteBytes(string name, byte[] bytes)
    {
        string path = Path.Combine(_temp.Path, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private string WriteZip(string name) => WriteBytes(name, [0x50, 0x4B, 0x03, 0x04, 0, 0, 0, 0]);

    private string WriteTar(string name)
    {
        byte[] header = new byte[512];
        byte[] ustar = System.Text.Encoding.ASCII.GetBytes("ustar");
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
    public async Task ListEntriesAsync_ZipFile_RoutesToZipService()
    {
        string zip = WriteZip("archive.zip");
        var zipService = new FakeArchiveService();
        var tarService = new FakeTarService();
        var router = new ArchiveListingRouter(zipService, tarService, AllSupported, new GroupPolicyOptions());

        ArchiveListResult result = await router.ListEntriesAsync(zip);

        zipService.ListCallCount.Should().Be(1);
        tarService.ListCallCount.Should().Be(0);
        zipService.LastListedPath.Should().Be(zip);
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ListEntriesAsync_TarFile_RoutesToTarService()
    {
        string tar = WriteTar("archive.tar");
        var zipService = new FakeArchiveService();
        var tarService = new FakeTarService();
        var router = new ArchiveListingRouter(zipService, tarService, AllSupported, new GroupPolicyOptions());

        ArchiveListResult result = await router.ListEntriesAsync(tar);

        tarService.ListCallCount.Should().Be(1);
        zipService.ListCallCount.Should().Be(0);
        tarService.LastListedPath.Should().Be(tar);
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ListEntriesAsync_RarUnsupportedByCapabilities_ReturnsFailureWithoutCallingEitherService()
    {
        string rar = WriteRar("archive.rar");
        var zipService = new FakeArchiveService();
        var tarService = new FakeTarService();
        TarCapabilities noRar = AllSupported with { SupportsRar = false };
        var router = new ArchiveListingRouter(zipService, tarService, noRar, new GroupPolicyOptions());

        ArchiveListResult result = await router.ListEntriesAsync(rar);

        zipService.ListCallCount.Should().Be(0);
        tarService.ListCallCount.Should().Be(0);
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("RAR");
    }

    // --- T-F250: listing is gated by Group Policy like every other operation ---

    [Fact]
    public async Task ListEntriesAsync_FormatBlockedByPolicy_ReturnsPolicyErrorWithoutCallingEitherService()
    {
        string rar = WriteRar("archive.rar");
        var zipService = new FakeArchiveService();
        var tarService = new FakeTarService();
        var router = new ArchiveListingRouter(zipService, tarService, AllSupported, new GroupPolicyOptions { BlockedFormats = ["rar"] });

        ArchiveListResult result = await router.ListEntriesAsync(rar);

        tarService.ListCallCount.Should().Be(0);
        zipService.ListCallCount.Should().Be(0);
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("This archive format (rar) is blocked by Group Policy.");
    }

    [Fact]
    public async Task ListEntriesAsync_TarDisabledByPolicy_ReturnsPolicyErrorWithoutCallingTar()
    {
        string tar = WriteTar("archive.tar");
        var tarService = new FakeTarService();
        var router = new ArchiveListingRouter(new FakeArchiveService(), tarService, AllSupported, new GroupPolicyOptions { DisableTarExtraction = true });

        ArchiveListResult result = await router.ListEntriesAsync(tar);

        tarService.ListCallCount.Should().Be(0);
        result.ErrorMessage.Should().Be("tar.exe-based extraction is disabled by Group Policy.");
    }

    [Fact]
    public async Task ListEntriesAsync_AllowedFormatsExcludesZip_ZipEngineNotCalled()
    {
        string zip = WriteZip("archive.zip");
        var zipService = new FakeArchiveService();
        var router = new ArchiveListingRouter(zipService, new FakeTarService(), AllSupported, new GroupPolicyOptions { AllowedFormats = ["tar"] });

        ArchiveListResult result = await router.ListEntriesAsync(zip);

        zipService.ListCallCount.Should().Be(0);
        result.ErrorMessage.Should().Contain("blocked by Group Policy");
    }

    [Fact]
    public async Task ListEntriesAsync_OtherFormatBlocked_StillListsTar()
    {
        string tar = WriteTar("archive.tar");
        var tarService = new FakeTarService();
        var router = new ArchiveListingRouter(new FakeArchiveService(), tarService, AllSupported, new GroupPolicyOptions { BlockedFormats = ["rar"] });

        ArchiveListResult result = await router.ListEntriesAsync(tar);

        tarService.ListCallCount.Should().Be(1);
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ListEntriesAsync_UnknownFormat_RoutesToZipService()
    {
        // Matches ExtractionRouter's own dispatch: an unrecognized format falls into the ZIP
        // bucket so ZipArchiveService's own defensive "not a real ZIP" path handles messaging.
        string unknown = WriteBytes("mystery.bin", [0x00, 0x01, 0x02, 0x03]);
        var zipService = new FakeArchiveService();
        var tarService = new FakeTarService();
        var router = new ArchiveListingRouter(zipService, tarService, AllSupported, new GroupPolicyOptions());

        await router.ListEntriesAsync(unknown);

        zipService.ListCallCount.Should().Be(1);
        tarService.ListCallCount.Should().Be(0);
    }
}
