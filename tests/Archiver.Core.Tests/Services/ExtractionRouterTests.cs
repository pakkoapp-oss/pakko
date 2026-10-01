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
    public ArchiveResult ExtractResult = new();
    // T-F306: what the engine reports while it runs.
    public IReadOnlyList<ProgressReport> ExtractProgressScript = [];
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
        foreach (ProgressReport report in ExtractProgressScript)
            progress?.Report(report);
        return Task.FromResult(ExtractResult);
    }

    // T-F261: what the router's TestAsync handed the ZIP engine.
    public IReadOnlyList<string>? LastTestedPaths;
    public int TestCallCount;
    public Func<PasswordPromptInfo, Task<PasswordDecision>>? LastTestResolver;
    public ArchiveResult TestResult = new();

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
    public ArchiveResult ExtractResult = new();
    // T-F306: what the engine reports while it runs.
    public IReadOnlyList<ProgressReport> ExtractProgressScript = [];
    public bool LastExtractReceivedNonNullProgress;

    public Task<TarCapabilities> DetectCapabilitiesAsync() => Task.FromResult(new TarCapabilities());

    public Task<ArchiveResult> ExtractAsync(ExtractOptions options, IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default)
    {
        ExtractCallCount++;
        LastExtractOptions = options;
        LastExtractReceivedNonNullProgress = progress != null;
        foreach (ProgressReport report in ExtractProgressScript)
            progress?.Report(report);
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
                CreatedFiles = ["zip-out"],
                Errors = [new ArchiveError { SourcePath = zip, Message = "zip error" }],
            }
        };
        var tarService = new FakeTarService
        {
            ExtractResult = new ArchiveResult
            {
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
        result.Outcome.Should().Be(OperationOutcome.Failed, "an error from either engine fails the merged result");
    }

    // T-F306: a mixed selection is one climb. ZIP runs first and tar second, each in its own
    // slice of the percent, sized by the archives' sizes on disk; tar's bytes continue from where
    // ZIP's ended, so neither the bar nor the byte count ever goes back. (T-F142 had left tar with
    // no progress at all here: the bar sat at 100% for the whole tar part.)
    [Fact]
    public async Task ExtractAsync_MixedSelection_OneClimbAcrossBothEngines()
    {
        string zip = WriteBytes("a.zip", [0x50, 0x4B, 0x03, 0x04, .. new byte[1532]]); // 1536 bytes: 75%
        string tar = WriteTar("b.tar"); // 512 bytes: 25%
        var zipService = new FakeArchiveService
        {
            ExtractProgressScript =
            [
                new ProgressReport { Percent = 50, BytesTransferred = 150, TotalBytes = 300, CurrentFile = "z.txt" },
                new ProgressReport { Percent = 100, BytesTransferred = 300, TotalBytes = 300 },
            ],
        };
        var tarService = new FakeTarService
        {
            ExtractProgressScript =
            [
                new ProgressReport { Percent = 0, BytesTransferred = 0, TotalBytes = 80 },
                new ProgressReport { Percent = 50, BytesTransferred = 40, TotalBytes = 80, CurrentFile = "t.txt" },
                new ProgressReport { Percent = 100, BytesTransferred = 80, TotalBytes = 80 },
            ],
        };
        var router = new ExtractionRouter(zipService, tarService, AllSupported, new GroupPolicyOptions());
        var reports = new List<ProgressReport>();

        await router.ExtractAsync(
            new ExtractOptions { ArchivePaths = [zip, tar], DestinationFolder = _temp.Path },
            new SynchronousProgress(reports.Add));

        reports.Select(r => (r.Percent, r.BytesTransferred, r.TotalBytes)).Should().Equal(
            (37, 150L, 300L), (75, 300L, 300L), (75, 300L, 380L), (87, 340L, 380L), (100, 380L, 380L));
        reports.Select(r => r.CurrentFile).Should().Equal("z.txt", null, null, "t.txt", null);
    }

    // Several archives of one kind report percent only (no byte total): the slice maps the percent
    // and keeps the report byte-less.
    [Fact]
    public async Task ExtractAsync_MixedSelection_PercentOnlyReportsStayByteless()
    {
        string zip = WriteBytes("a.zip", [0x50, 0x4B, 0x03, 0x04, .. new byte[508]]);
        string tar = WriteTar("b.tar");
        var zipService = new FakeArchiveService { ExtractProgressScript = [new ProgressReport { Percent = 100 }] };
        var tarService = new FakeTarService { ExtractProgressScript = [new ProgressReport { Percent = 50 }, new ProgressReport { Percent = 100 }] };
        var router = new ExtractionRouter(zipService, tarService, AllSupported, new GroupPolicyOptions());
        var reports = new List<ProgressReport>();

        await router.ExtractAsync(
            new ExtractOptions { ArchivePaths = [zip, tar], DestinationFolder = _temp.Path },
            new SynchronousProgress(reports.Add));

        reports.Select(r => (r.Percent, r.BytesTransferred, r.TotalBytes)).Should().Equal((50, 0L, 0L), (75, 0L, 0L), (100, 0L, 0L));
    }

    // One kind only: the engine's reports pass through untouched.
    [Fact]
    public async Task ExtractAsync_PureTarSelection_ReportsPassThroughUnchanged()
    {
        string tar = WriteTar("b.tar");
        var report = new ProgressReport { Percent = 40, BytesTransferred = 4, TotalBytes = 10, CurrentFile = "t.txt" };
        var tarService = new FakeTarService { ExtractProgressScript = [report] };
        var router = new ExtractionRouter(new FakeArchiveService(), tarService, AllSupported, new GroupPolicyOptions());
        var reports = new List<ProgressReport>();

        await router.ExtractAsync(
            new ExtractOptions { ArchivePaths = [tar], DestinationFolder = _temp.Path }, new SynchronousProgress(reports.Add));

        reports.Should().Equal(report);
    }

    private sealed class SynchronousProgress(Action<ProgressReport> onReport) : IProgress<ProgressReport>
    {
        public void Report(ProgressReport value) => onReport(value);
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
        var zipError = new ArchiveError { SourcePath = zip, Message = "zip engine error" };
        var zipService = new FakeArchiveService { TestResult = new ArchiveResult { Errors = [zipError], SkippedFiles = [zipSkip] } };
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
