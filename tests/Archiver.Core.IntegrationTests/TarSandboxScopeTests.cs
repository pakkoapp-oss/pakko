using Archiver.Core.Services.Sandbox;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// Step 6 of T-F52's build order: end-to-end proof that TarSandboxScope ties profile + ACL +
/// staging + Job Object together correctly — the same shape TarSandboxedService will use for
/// both the pre-scan and extraction within one scope. The quarantine is rooted under
/// %TEMP%\PakkoTarSandbox\, not next to the (here, arbitrary TempDirectory-based) destination —
/// see DECISIONS.md's T-F52 entry for why "same disk as destination" was dropped after an
/// AppContainer ancestor-traversal failure was found empirically.
/// </summary>
[Collection("TarSandbox")]
public sealed class TarSandboxScopeTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task RunAsync_PreScanThenExtractionWithinOneScope_BothSucceed()
    {
        string archivePath = Path.Combine(_temp.Path, "fixture.tar");
        ExternalTarFixtureBuilder.CreateCompressedTar(archivePath, "-cf", [("a.txt", "scope test content")]);

        using var scope = await TarSandboxScope.CreateAsync(archivePath, needsOutputDir: true, CancellationToken.None);

        var (preScanExit, preScanStdOut, preScanStdErr) = await scope.RunAsync(
            ["-tf", scope.StagedArchivePath], CancellationToken.None);
        preScanExit.Should().Be(0, because: preScanStdErr);
        preScanStdOut.Should().Contain("a.txt");

        var (extractExit, _, extractStdErr) = await scope.RunAsync(
            ["-xf", scope.StagedArchivePath, "-C", scope.OutputDirectory!], CancellationToken.None);
        extractExit.Should().Be(0, because: extractStdErr);

        File.ReadAllText(Path.Combine(scope.OutputDirectory!, "a.txt")).Should().Be("scope test content");
    }

    [Fact]
    public async Task CreateAsync_NeedsOutputDirFalse_NoOutFolderCreated()
    {
        string archivePath = Path.Combine(_temp.Path, "fixture.tar");
        ExternalTarFixtureBuilder.CreateCompressedTar(archivePath, "-cf", [("a.txt", "listing only")]);

        using var scope = await TarSandboxScope.CreateAsync(archivePath, needsOutputDir: false, CancellationToken.None);

        scope.OutputDirectory.Should().BeNull();
        Directory.Exists(Path.Combine(scope.QuarantineRoot, "out")).Should().BeFalse();

        var (exitCode, stdOut, stdErr) = await scope.RunAsync(["-tf", scope.StagedArchivePath], CancellationToken.None);
        exitCode.Should().Be(0, because: stdErr);
        stdOut.Should().Contain("a.txt");
    }

    [Fact]
    public async Task CreateAsync_StagedArchiveIsHardlinkedSameVolume_StillReadableInsideSandbox()
    {
        // Regression test for a real bug found empirically: NTFS hard links share their
        // security descriptor with the ORIGINAL file, not the containing directory — so a
        // hardlinked staged archive was unreadable to the AppContainer even though "in\" itself
        // was correctly ACL'd, until TarSandboxScope started granting Read&Execute on the staged
        // file path directly too (see DECISIONS.md's T-F52 entry). This test only means anything
        // when the source archive and %TEMP% are on the same volume (so QuarantineStaging
        // actually hardlinks rather than copies) — true for essentially every real machine.
        string archivePath = Path.Combine(_temp.Path, "fixture.tar");
        ExternalTarFixtureBuilder.CreateCompressedTar(archivePath, "-cf", [("a.txt", "hardlink regression")]);

        QuarantineStaging.IsSameVolume(archivePath, Path.GetTempPath()).Should().BeTrue(
            "this regression only reproduces when staging actually hardlinks — adjust the test environment if this ever fails");

        using var scope = await TarSandboxScope.CreateAsync(archivePath, needsOutputDir: true, CancellationToken.None);

        var (exitCode, stdOut, stdErr) = await scope.RunAsync(["-tf", scope.StagedArchivePath], CancellationToken.None);

        exitCode.Should().Be(0, because: stdErr);
        stdOut.Should().Contain("a.txt");
    }

    [Fact]
    public async Task Dispose_DeletesQuarantineDirectoryButNotAppContainerProfile()
    {
        string archivePath = Path.Combine(_temp.Path, "fixture.tar");
        ExternalTarFixtureBuilder.CreateCompressedTar(archivePath, "-cf", [("a.txt", "dispose test")]);

        var scope = await TarSandboxScope.CreateAsync(archivePath, needsOutputDir: true, CancellationToken.None);
        string quarantineRoot = scope.QuarantineRoot;
        Directory.Exists(quarantineRoot).Should().BeTrue();

        scope.Dispose();

        Directory.Exists(quarantineRoot).Should().BeFalse();

        // The shared production profile must still exist and be reusable — Dispose() must never
        // delete it (see DECISIONS.md's T-F52 follow-up entry: create-once-reuse-forever).
        var profile = new AppContainerProfile(AppContainerProfile.ProductionProfileName);
        Action reuseProfile = () => profile.EnsureExists();
        reuseProfile.Should().NotThrow();
    }
}
