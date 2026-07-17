using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// T-F98 (Archive Browser nested-archive drill-down): proves the compression-bomb check
/// (T-F90/T-F94) and the whole-archive pre-scan (T-F49) apply independently at a SECOND nesting
/// level, not just the outermost archive. Drilling into a nested archive is, at the Core layer,
/// nothing more than TarSandboxedService.ExtractAsync called again against whatever was just
/// extracted — these tests exercise exactly that two-call shape (no App-layer/ViewModel code is
/// involved) to confirm nesting doesn't accidentally weaken either mechanism.
/// </summary>
public sealed class NestedArchiveDrillDownSecurityTests : IDisposable
{
    private readonly TarSandboxedService _sut = new();
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Integration]
    public async Task ExtractAsync_NestedBombArchive_RejectedIndependentlyAtSecondLevel()
    {
        // Level 0 -> 1: build an outer tar whose only entry is itself a real bomb-shaped tar.gz.
        string nestedBombPath = Path.Combine(_temp.Path, "inner_bomb.tar.gz");
        string bombContent = new string('A', 5_000_000);
        ExternalTarFixtureBuilder.CreateCompressedTar(nestedBombPath, "-czf", [("bomb.txt", bombContent)]);
        byte[] nestedBombBytes = File.ReadAllBytes(nestedBombPath);

        string outerArchivePath = Path.Combine(_temp.Path, "outer.tar");
        TarBuilder.WriteTar(outerArchivePath,
        [
            new TarBuilder.Entry { Name = "inner_bomb.tar.gz", Content = nestedBombBytes },
        ]);

        // Level 1: drill into the outer archive — extracts just the nested archive to a scope dir,
        // exactly as NavigateIntoNestedArchiveAsync does. The outer archive itself isn't bomb-shaped,
        // so this must succeed normally.
        string scopeDir = Path.Combine(_temp.Path, "scope1");
        var level1Result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [outerArchivePath],
            DestinationFolder = scopeDir,
            Mode = ExtractMode.SingleFolder,
            SelectedEntryPaths = ["inner_bomb.tar.gz"],
        });

        level1Result.Success.Should().BeTrue();
        string extractedNestedPath = Path.Combine(scopeDir, "inner_bomb.tar.gz");
        File.Exists(extractedNestedPath).Should().BeTrue();

        // Level 2: drill into the now-extracted nested archive itself — the whole-archive
        // compression-ratio check (T-F90/T-F94) must fire again here, independently of the outer
        // level having been clean, with no confirm callback wired (auto-decline default).
        string destDir = Path.Combine(_temp.Path, "out");
        var level2Result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [extractedNestedPath],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
        });

        level2Result.Success.Should().BeTrue();
        level2Result.Errors.Should().BeEmpty();
        level2Result.CreatedFiles.Should().BeEmpty();
        level2Result.SkippedFiles.Should().Contain(s => s.Path == extractedNestedPath);
        File.Exists(Path.Combine(destDir, "bomb.txt")).Should().BeFalse();
    }
}
