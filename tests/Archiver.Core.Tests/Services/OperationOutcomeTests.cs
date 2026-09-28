using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F260 (phase 7) / T-F274: one classifier for what an operation achieved, so no frontend decides
// "success", "partly done" or "nothing happened" for itself.
public sealed class OperationOutcomeTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static readonly ArchiveError AnError = new() { SourcePath = "a", Message = "boom" };
    private static readonly SkippedFile ASkip = new() { Path = "a", Reason = "skipped" };

    [Fact]
    public void Empty_IsCompleted() =>
        new ArchiveResult().Outcome.Should().Be(OperationOutcome.Completed);

    [Fact]
    public void AnyError_IsFailed_AndNotSuccess()
    {
        var result = new ArchiveResult { Errors = [AnError], CreatedFiles = ["out.zip"] };

        result.Outcome.Should().Be(OperationOutcome.Failed);
        result.Success.Should().BeFalse();
    }

    [Fact]
    public void SkipsWithOutput_IsCompletedWithSkips()
    {
        var result = new ArchiveResult { SkippedFiles = [ASkip], CreatedFiles = ["out.zip"] };

        result.Outcome.Should().Be(OperationOutcome.CompletedWithSkips);
        result.Success.Should().BeTrue();
    }

    [Fact]
    public void SkipsWithAProcessedSource_IsCompletedWithSkips()
    {
        var result = new ArchiveResult
        {
            SkippedFiles = [ASkip],
            Sources = [new SourceResult { Path = "t.zip", Outcome = SourceOutcome.Completed }],
        };

        result.Outcome.Should().Be(OperationOutcome.CompletedWithSkips);
    }

    [Fact]
    public void OnlySkips_IsNothingDone()
    {
        var result = new ArchiveResult
        {
            SkippedFiles = [ASkip],
            Sources = [new SourceResult { Path = "t.zip", Outcome = SourceOutcome.NotProcessed }],
        };

        result.Outcome.Should().Be(OperationOutcome.NothingDone);
    }

    [Fact]
    public async Task Test_ValidZip_IsCompleted()
    {
        ArchiveResult result = await new ZipArchiveService(new GroupPolicyOptions())
            .TestAsync([FixtureHelper.Archive("valid_single_file.zip")]);

        result.Outcome.Should().Be(OperationOutcome.Completed);
    }

    [Fact]
    public async Task Test_OnlyATarArchive_IsNothingDone()
    {
        string tar = FixtureHelper.Archive("valid_nested_folders.tar");
        var router = new ExtractionRouter(new ZipArchiveService(new GroupPolicyOptions()), new TarSandboxedService(new GroupPolicyOptions()),
            new TarCapabilities(), new GroupPolicyOptions());

        ArchiveResult result = await router.TestAsync([tar]);

        result.Outcome.Should().Be(OperationOutcome.NothingDone, "nothing was tested, so no \"no errors\" claim");
    }

    [Fact]
    public async Task Test_ZipAndTar_IsCompletedWithSkips()
    {
        var router = new ExtractionRouter(new ZipArchiveService(new GroupPolicyOptions()), new TarSandboxedService(new GroupPolicyOptions()),
            new TarCapabilities(), new GroupPolicyOptions());

        ArchiveResult result = await router.TestAsync(
            [FixtureHelper.Archive("valid_single_file.zip"), FixtureHelper.Archive("valid_nested_folders.tar")]);

        result.Outcome.Should().Be(OperationOutcome.CompletedWithSkips);
    }

    [Fact]
    public async Task Test_BlockedZip_IsNothingDone()
    {
        ArchiveResult result = await new ZipArchiveService(new GroupPolicyOptions { BlockedFormats = ["zip"] })
            .TestAsync([FixtureHelper.Archive("valid_single_file.zip")]);

        result.Outcome.Should().Be(OperationOutcome.NothingDone);
    }

    [Fact]
    public async Task Extract_EverythingAlreadyThere_IsNothingDone()
    {
        var sut = new ZipArchiveService(new GroupPolicyOptions());
        string zip = FixtureHelper.Archive("valid_single_file.zip");
        var options = new ExtractOptions
        {
            ArchivePaths = [zip],
            DestinationFolder = _temp.Path,
            Mode = ExtractMode.SingleFolder,
            OnConflict = ConflictBehavior.Skip,
        };
        await sut.ExtractAsync(options);

        ArchiveResult again = await sut.ExtractAsync(options);

        again.Outcome.Should().Be(OperationOutcome.NothingDone);
    }
}
