using System.IO.Compression;
using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.CLI.Tests;

// T-F415: -aos is the user's own choice - a file kept because it exists exits 0, as in 7-Zip;
// the default skip keeps T-F313's exit 1, and every other skip keeps its code.
public sealed class CliExitCodeTests : IAsyncLifetime
{
    private readonly TestTempFolder _temp = new("pakko-exitcode-");

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _temp.DeleteAsync();

    // A real ZIP run in-process: a.txt is on disk already, b.txt is not (or is too, with allExist).
    private async Task<ArchiveResult> ExtractOverExistingAsync(bool allExist = false)
    {
        string source = Directory.CreateDirectory(Path.Combine(_temp.Path, "src")).FullName;
        File.WriteAllText(Path.Combine(source, "a.txt"), "a");
        File.WriteAllText(Path.Combine(source, "b.txt"), "b");
        string zip = Path.Combine(_temp.Path, "x.zip");
        ZipFile.CreateFromDirectory(source, zip);
        string destination = Directory.CreateDirectory(Path.Combine(_temp.Path, "out")).FullName;
        File.WriteAllText(Path.Combine(destination, "a.txt"), "kept");
        if (allExist)
            File.WriteAllText(Path.Combine(destination, "b.txt"), "kept");

        var services = PakkoServices.Create(new GroupPolicyOptions());
        return await services.ArchiveService.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [zip],
            DestinationFolder = destination,
            Mode = ExtractMode.SingleFolder,
            OnConflict = ConflictBehavior.Skip,
        });
    }

    // The tar engine lists a kept file as a skip of the same code, not apart.
    private static ArchiveResult AsTarReportsIt(ArchiveResult zipResult) =>
        zipResult with { SkippedFiles = [.. zipResult.SkippedFiles, .. zipResult.KeptExistingFiles], KeptExistingFiles = [] };

    private static readonly SkippedFile OtherSkip = new() { Path = "c.lnk", Reason = "A link is not archived." };

    [Fact]
    public async Task KeptBecauseItExists_WithAos_IsZero()
    {
        ArchiveResult result = await ExtractOverExistingAsync();
        result.KeptExistingFiles.Should().ContainSingle();

        CliExitCode.For(result, keptExistingByChoice: true).Should().Be(0);
    }

    [Fact]
    public async Task KeptBecauseItExists_WithoutAos_IsOne()
    {
        ArchiveResult result = await ExtractOverExistingAsync();

        CliExitCode.For(result, keptExistingByChoice: false).Should().Be(1);
    }

    [Fact]
    public async Task KeptBecauseItExists_OnTheTarRoute_WithAos_IsZero()
    {
        ArchiveResult result = AsTarReportsIt(await ExtractOverExistingAsync());
        result.Outcome.Should().Be(OperationOutcome.CompletedWithSkips);

        CliExitCode.For(result, keptExistingByChoice: true).Should().Be(0);
        CliExitCode.For(result, keptExistingByChoice: false).Should().Be(1);
    }

    // Scoop's re-run: every file is there already. ZIP then also says the archive gave nothing.
    [Fact]
    public async Task EveryFileKept_WithAos_IsZero()
    {
        ArchiveResult result = await ExtractOverExistingAsync(allExist: true);
        result.SkippedFiles.Should().ContainSingle(s => s.Text!.Code == MessageCode.AllEntriesSkipped);

        CliExitCode.For(result, keptExistingByChoice: true).Should().Be(0);
        CliExitCode.For(result, keptExistingByChoice: false).Should().Be(1);
    }

    [Fact]
    public async Task KeptBecauseItExists_WithAos_AndAnError_IsTwo()
    {
        ArchiveResult result = await ExtractOverExistingAsync();
        result = result with { Errors = [new ArchiveError { SourcePath = "x.zip", Message = "CRC failed" }] };

        CliExitCode.For(result, keptExistingByChoice: true).Should().Be(2);
    }

    // A skip for any other reason (a link, an unsafe entry, a format Pakko cannot open) is not what
    // -aos asked for: it still says that not everything was extracted.
    [Fact]
    public async Task KeptBecauseItExists_WithAos_AndAnotherSkip_IsOne()
    {
        ArchiveResult result = await ExtractOverExistingAsync();
        result = result with { SkippedFiles = [.. result.SkippedFiles, OtherSkip] };

        CliExitCode.For(result, keptExistingByChoice: true).Should().Be(1);
    }

    [Fact]
    public async Task EveryFileKept_WithAos_AndAnotherSkip_IsOne()
    {
        ArchiveResult result = await ExtractOverExistingAsync(allExist: true);
        result = result with { SkippedFiles = [.. result.SkippedFiles, OtherSkip] };

        CliExitCode.For(result, keptExistingByChoice: true).Should().Be(1);
    }

    // An archive with no entry at all is no kept file: -aos does not explain it.
    [Fact]
    public async Task AllEntriesSkippedWithNothingKept_WithAos_IsOne()
    {
        ArchiveResult result = await ExtractOverExistingAsync(allExist: true);
        var empty = new ArchiveResult { SkippedFiles = [result.SkippedFiles.Single(s => s.Text!.Code == MessageCode.AllEntriesSkipped)] };

        CliExitCode.For(empty, keptExistingByChoice: true).Should().Be(1);
    }

    [Fact]
    public void AnotherSkipAlone_WithAos_IsOne() =>
        CliExitCode.For(new ArchiveResult { SkippedFiles = [OtherSkip] }, keptExistingByChoice: true).Should().Be(1);

    [Fact]
    public void CleanResult_IsZero() =>
        CliExitCode.For(new ArchiveResult(), keptExistingByChoice: true).Should().Be(0);
}
