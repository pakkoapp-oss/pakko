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
    // T-F142: records whether ExtractionRouter passed a real progress sink through, so tests can
    // assert the mixed-selection real-byte-progress suppression without needing an actual
    // IProgress<T> implementation.
    public bool LastExtractReceivedNonNullProgress;

    public Task<ArchiveResult> ArchiveAsync(ArchiveOptions options, IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<ArchiveResult> ExtractAsync(ExtractOptions options, IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default)
    {
        ExtractCallCount++;
        LastExtractOptions = options;
        LastExtractReceivedNonNullProgress = progress != null;
        return Task.FromResult(ExtractResult);
    }

    // T-F261: what the router's TestAsync handed the ZIP engine.
    public IReadOnlyList<string>? LastTestedPaths;
    public int TestCallCount;
    public Func<PasswordPromptInfo, Task<PasswordDecision>>? LastTestResolver;
    public ArchiveResult TestResult = new() { Success = true };

    public Task<ArchiveResult> TestAsync(IReadOnlyList<string> archivePaths, IProgress<ProgressReport>? progress = null, Func<PasswordPromptInfo, Task<PasswordDecision>>? resolvePasswordAsync = null, CancellationToken cancellationToken = default)
    {
        TestCallCount++;
        LastTestedPaths = archivePaths;
        LastTestResolver = resolvePasswordAsync;
        return Task.FromResult(TestResult);
    }

    public Task<ArchiveListResult> ListEntriesAsync(string archivePath, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}

file sealed class FakeTarService : ITarService
{
    public ExtractOptions? LastExtractOptions;
    public int ExtractCallCount;
    public ArchiveResult ExtractResult = new() { Success = true };
    public bool LastExtractReceivedNonNullProgress;

    public Task<TarCapabilities> DetectCapabilitiesAsync() => Task.FromResult(new TarCapabilities());

    public Task<ArchiveResult> ExtractAsync(ExtractOptions options, IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default)
    {
        ExtractCallCount++;
        LastExtractOptions = options;
        LastExtractReceivedNonNullProgress = progress != null;
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
    public async Task ExtractAsync_PureZipSelection_OnlyCallsZipService()
    {
        string zip = WriteBytes("only.zip", [0x50, 0x4B, 0x03, 0x04, 0, 0, 0, 0]);
        var zipService = new FakeArchiveService();
        var tarService = new FakeTarService();
        var router = new ExtractionRouter(zipService, tarService, AllSupported, new GroupPolicyOptions());

        ArchiveResult result = await router.ExtractAsync(new ExtractOptions { ArchivePaths = [zip], DestinationFolder = _temp.Path });

        zipService.ExtractCallCount.Should().Be(1);
        tarService.ExtractCallCount.Should().Be(0);
        zipService.LastExtractOptions!.ArchivePaths.Should().BeEquivalentTo([zip]);
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ExtractAsync_PureTarSelection_OnlyCallsTarService()
    {
        string tar = WriteTar("only.tar");
        var zipService = new FakeArchiveService();
        var tarService = new FakeTarService();
        var router = new ExtractionRouter(zipService, tarService, AllSupported, new GroupPolicyOptions());

        ArchiveResult result = await router.ExtractAsync(new ExtractOptions { ArchivePaths = [tar], DestinationFolder = _temp.Path });

        tarService.ExtractCallCount.Should().Be(1);
        zipService.ExtractCallCount.Should().Be(0);
        tarService.LastExtractOptions!.ArchivePaths.Should().BeEquivalentTo([tar]);
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ExtractAsync_MixedSelection_CallsBothAndMergesResults()
    {
        string zip = WriteZip("a.zip");
        string tar = WriteTar("b.tar");

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
        var router = new ExtractionRouter(zipService, tarService, AllSupported, new GroupPolicyOptions());

        ArchiveResult result = await router.ExtractAsync(new ExtractOptions { ArchivePaths = [zip, tar], DestinationFolder = _temp.Path });

        zipService.ExtractCallCount.Should().Be(1);
        tarService.ExtractCallCount.Should().Be(1);
        zipService.LastExtractOptions!.ArchivePaths.Should().BeEquivalentTo([zip]);
        tarService.LastExtractOptions!.ArchivePaths.Should().BeEquivalentTo([tar]);
        result.CreatedFiles.Should().BeEquivalentTo(["zip-out", "tar-out"]);
        result.Errors.Should().HaveCount(1);
        result.SkippedFiles.Should().HaveCount(1);
        result.Success.Should().BeTrue();
    }

    // T-F142 regression: found via advisor review before this shipped. TarSandboxedService now
    // gives real byte-level progress to a single-archive extraction (tarPaths.Count == 1) — but a
    // MIXED selection of exactly one zip + one tar means BOTH services independently believe
    // themselves "the sole archive" (each only sees its own bucket's count). Since zip always runs
    // first and already used real progress before this task, letting tar ALSO believe itself alone
    // would restart a second real 0->100 climb after zip's had already finished — a visible dip
    // back down in the dialog. ExtractionRouter must suppress tar's real progress whenever zip also
    // ran (zipPaths non-empty), while leaving zip's own (pre-existing, unconditional) progress
    // pass-through untouched.
    [Fact]
    public async Task ExtractAsync_MixedSelectionOfExactlyOneZipAndOneTar_SuppressesTarRealProgressNotZips()
    {
        string zip = WriteZip("a.zip");
        string tar = WriteTar("b.tar");

        var zipService = new FakeArchiveService();
        var tarService = new FakeTarService();
        var router = new ExtractionRouter(zipService, tarService, AllSupported, new GroupPolicyOptions());

        var progress = new Progress<ProgressReport>(_ => { });
        await router.ExtractAsync(
            new ExtractOptions { ArchivePaths = [zip, tar], DestinationFolder = _temp.Path }, progress);

        zipService.LastExtractReceivedNonNullProgress.Should().BeTrue(
            "zip's own progress pass-through is unconditional and pre-existing — this task must not change it");
        tarService.LastExtractReceivedNonNullProgress.Should().BeFalse(
            "tar must not receive real progress when zip also ran in the same mixed selection, or its own " +
            "single-archive real-byte climb would restart and visibly dip the dialog after zip already reached 100%");
    }

    // T-F142: the flip side of the mixed-selection test above — a PURE tar-only selection (no zip
    // bucket at all) must still get real progress, proving the mixed-selection suppression above is
    // scoped correctly and doesn't regress the common single-format case.
    [Fact]
    public async Task ExtractAsync_PureTarSelection_StillReceivesRealProgress()
    {
        string tar = WriteTar("b.tar");
        var zipService = new FakeArchiveService();
        var tarService = new FakeTarService();
        var router = new ExtractionRouter(zipService, tarService, AllSupported, new GroupPolicyOptions());

        var progress = new Progress<ProgressReport>(_ => { });
        await router.ExtractAsync(
            new ExtractOptions { ArchivePaths = [tar], DestinationFolder = _temp.Path }, progress);

        tarService.LastExtractReceivedNonNullProgress.Should().BeTrue();
    }

    [Fact]
    public async Task ExtractAsync_RarUnsupportedByCapabilities_SkipsWithoutCallingEitherService()
    {
        string rar = WriteRar("only.rar");
        var zipService = new FakeArchiveService();
        var tarService = new FakeTarService();
        TarCapabilities noRar = AllSupported with { SupportsRar = false };
        var router = new ExtractionRouter(zipService, tarService, noRar, new GroupPolicyOptions());

        ArchiveResult result = await router.ExtractAsync(new ExtractOptions { ArchivePaths = [rar], DestinationFolder = _temp.Path });

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
        string zip = WriteZip("a.zip");
        string tar = WriteTar("b.tar");
        var zipService = new FakeArchiveService();
        var tarService = new FakeTarService();
        var router = new ExtractionRouter(zipService, tarService, AllSupported, new GroupPolicyOptions());

        await router.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [zip, tar],
            DestinationFolder = _temp.Path,
        });

        zipService.LastExtractOptions!.OpenDestinationFolder.Should().BeFalse();
        tarService.LastExtractOptions!.OpenDestinationFolder.Should().BeFalse();
    }

    [Fact]
    public async Task ExtractAsync_DisableTarExtractionPolicy_SkipsTarButStillExtractsZip()
    {
        string zip = WriteZip("a.zip");
        string tar = WriteTar("b.tar");
        var zipService = new FakeArchiveService();
        var tarService = new FakeTarService();
        var policy = new GroupPolicyOptions { DisableTarExtraction = true };
        var router = new ExtractionRouter(zipService, tarService, AllSupported, policy);

        ArchiveResult result = await router.ExtractAsync(new ExtractOptions { ArchivePaths = [zip, tar], DestinationFolder = _temp.Path });

        zipService.ExtractCallCount.Should().Be(1);
        tarService.ExtractCallCount.Should().Be(0);
        result.SkippedFiles.Should().ContainSingle(s => s.Path == tar && s.Reason.Contains("Group Policy"));
    }

    [Fact]
    public async Task ExtractAsync_BlockedFormatsPolicy_SkipsBlockedFormatOnly()
    {
        string zip = WriteZip("a.zip");
        string tar = WriteTar("b.tar");
        var zipService = new FakeArchiveService();
        var tarService = new FakeTarService();
        var policy = new GroupPolicyOptions { BlockedFormats = ["zip"] };
        var router = new ExtractionRouter(zipService, tarService, AllSupported, policy);

        ArchiveResult result = await router.ExtractAsync(new ExtractOptions { ArchivePaths = [zip, tar], DestinationFolder = _temp.Path });

        zipService.ExtractCallCount.Should().Be(0);
        tarService.ExtractCallCount.Should().Be(1);
        result.SkippedFiles.Should().ContainSingle(s => s.Path == zip && s.Reason.Contains("Group Policy"));
    }

    [Fact]
    public async Task ExtractAsync_AllowedFormatsPolicy_SkipsEverythingNotListed()
    {
        string zip = WriteZip("a.zip");
        string tar = WriteTar("b.tar");
        var zipService = new FakeArchiveService();
        var tarService = new FakeTarService();
        var policy = new GroupPolicyOptions { AllowedFormats = ["tar"] };
        var router = new ExtractionRouter(zipService, tarService, AllSupported, policy);

        ArchiveResult result = await router.ExtractAsync(new ExtractOptions { ArchivePaths = [zip, tar], DestinationFolder = _temp.Path });

        zipService.ExtractCallCount.Should().Be(0);
        tarService.ExtractCallCount.Should().Be(1);
        result.SkippedFiles.Should().ContainSingle(s => s.Path == zip && s.Reason.Contains("Group Policy"));
    }

    [Fact]
    public async Task ExtractAsync_BlockedFormatsTakesPrecedenceOverAllowedFormats()
    {
        string rar = WriteRar("only.rar");
        var zipService = new FakeArchiveService();
        var tarService = new FakeTarService();
        var policy = new GroupPolicyOptions { AllowedFormats = ["rar"], BlockedFormats = ["rar"] };
        var router = new ExtractionRouter(zipService, tarService, AllSupported, policy);

        ArchiveResult result = await router.ExtractAsync(new ExtractOptions { ArchivePaths = [rar], DestinationFolder = _temp.Path });

        tarService.ExtractCallCount.Should().Be(0);
        result.SkippedFiles.Should().ContainSingle(s => s.Path == rar && s.Reason.Contains("Group Policy"));
    }

    // --- T-F261: TestAsync goes through the router ---

    [Fact]
    public async Task TestAsync_ZipAndTar_TestsOnlyTheZipAndSkipsTheTarWithoutTouchingTar()
    {
        string zip = WriteZip("a.zip");
        string tar = WriteTar("b.tar");
        var zipService = new FakeArchiveService();
        var tarService = new FakeTarService();
        var router = new ExtractionRouter(zipService, tarService, AllSupported, new GroupPolicyOptions());

        ArchiveResult result = await router.TestAsync([zip, tar]);

        zipService.LastTestedPaths.Should().Equal(zip);
        tarService.ExtractCallCount.Should().Be(0);
        SkippedFile skipped = result.SkippedFiles.Should().ContainSingle().Subject;
        (skipped.Path, skipped.Reason, skipped.Text!.Code)
            .Should().Be((tar, ArchiveFormatPolicy.NoTestCapabilityReason, MessageCode.NoTestCapability));
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task TestAsync_ZipBlockedByPolicy_SkippedWithPolicyReasonAndEngineNotCalled()
    {
        string zip = WriteZip("a.zip");
        var zipService = new FakeArchiveService();
        var router = new ExtractionRouter(zipService, new FakeTarService(), AllSupported, new GroupPolicyOptions { BlockedFormats = ["zip"] });

        ArchiveResult result = await router.TestAsync([zip]);

        zipService.TestCallCount.Should().Be(0);
        result.SkippedFiles.Should().ContainSingle(s => s.Path == zip && s.Reason.Contains("blocked by Group Policy"));
    }

    [Fact]
    public async Task TestAsync_TarDisabledByPolicy_NamesThePolicyNotTheMissingTestMode()
    {
        string rar = WriteRar("a.rar");
        var zipService = new FakeArchiveService();
        var router = new ExtractionRouter(zipService, new FakeTarService(), new TarCapabilities(), new GroupPolicyOptions { DisableTarExtraction = true });

        ArchiveResult result = await router.TestAsync([rar]);

        zipService.TestCallCount.Should().Be(0);
        result.SkippedFiles.Should().ContainSingle(s => s.Path == rar && s.Reason == "tar.exe-based extraction is disabled by Group Policy.");
    }

    [Fact]
    public async Task TestAsync_UnrecognizedFile_StillGoesToTheZipEngine()
    {
        string unknown = WriteBytes("notes.txt", "plain text"u8.ToArray());
        var zipService = new FakeArchiveService();
        var router = new ExtractionRouter(zipService, new FakeTarService(), AllSupported, new GroupPolicyOptions());

        await router.TestAsync([unknown]);

        zipService.LastTestedPaths.Should().Equal(unknown);
    }

    [Fact]
    public async Task TestAsync_PasswordCallback_ReachesTheZipEngineAndZipSkipsAreKept()
    {
        string zip = WriteZip("a.zip");
        string tar = WriteTar("b.tar");
        var zipSkip = new SkippedFile { Path = zip, Reason = "zip engine skip" };
        var zipService = new FakeArchiveService { TestResult = new ArchiveResult { Success = false, SkippedFiles = [zipSkip] } };
        var router = new ExtractionRouter(zipService, new FakeTarService(), AllSupported, new GroupPolicyOptions());
        Func<PasswordPromptInfo, Task<PasswordDecision>> resolver = _ => Task.FromResult(new PasswordDecision());

        ArchiveResult result = await router.TestAsync([zip, tar], resolvePasswordAsync: resolver);

        zipService.LastTestResolver.Should().BeSameAs(resolver);
        result.Success.Should().BeFalse();
        result.SkippedFiles.Select(s => s.Path).Should().Equal(zip, tar);
    }

    [Fact]
    public async Task TestAsync_OnlyTarFamily_NeverCallsTheZipEngine()
    {
        string tar = WriteTar("b.tar");
        var zipService = new FakeArchiveService();
        var router = new ExtractionRouter(zipService, new FakeTarService(), AllSupported, new GroupPolicyOptions());

        ArchiveResult result = await router.TestAsync([tar]);

        zipService.TestCallCount.Should().Be(0);
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ExtractAsync_EmptyPolicy_BehavesAsUnrestricted()
    {
        string zip = WriteZip("a.zip");
        var zipService = new FakeArchiveService();
        var tarService = new FakeTarService();
        var router = new ExtractionRouter(zipService, tarService, AllSupported, new GroupPolicyOptions());

        ArchiveResult result = await router.ExtractAsync(new ExtractOptions { ArchivePaths = [zip], DestinationFolder = _temp.Path });

        zipService.ExtractCallCount.Should().Be(1);
        result.Success.Should().BeTrue();
    }
}
