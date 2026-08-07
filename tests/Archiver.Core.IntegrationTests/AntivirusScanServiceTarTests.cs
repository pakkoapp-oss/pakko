using System.Text;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Services.Antivirus;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

// Hand-rolled fake — no mocking library is used anywhere in this repo. Duplicated from
// Archiver.Core.Tests' own AntivirusScanServiceTests rather than shared: this project's fakes
// (TarBuilder aside) are consistently per-file, and a real cross-project shared test helper
// library doesn't exist in this repo yet.
internal sealed class FakeAmsiScanner : IAmsiScanner
{
    public HashSet<string> DetectedContentNames { get; } = new(StringComparer.Ordinal);
    public List<string> ScannedContentNames { get; } = [];
    public bool Disposed { get; private set; }

    public (ThreatVerdict Verdict, string? ThreatName) ScanBuffer(byte[] buffer, int length, string contentName)
    {
        ScannedContentNames.Add(contentName);
        return DetectedContentNames.Contains(contentName)
            ? (ThreatVerdict.ThreatDetected, "Fake-Test-Threat")
            : (ThreatVerdict.Clean, null);
    }

    public void Dispose() => Disposed = true;
}

/// <summary>
/// T-F146: exercises AntivirusScanService's tar-family path against the real system tar.exe inside
/// the real AppContainer sandbox (TarSandboxScope) — the same machinery T-F49/T-F52's extraction
/// tests already prove, but here stopping at the quarantine "out\" directory instead of moving
/// files to a real destination. [Collection("TarSandbox")] serializes this against every other
/// real-sandbox test class (T-F130) since they all share one AppContainer profile/quarantine root.
/// </summary>
[Collection("TarSandbox")]
public sealed class AntivirusScanServiceTarTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static AntivirusScanService CreateService(FakeAmsiScanner scanner) =>
        new(new TarCapabilities(), groupPolicyOptions: null, () => scanner, isProviderRegistered: () => true);

    [Integration]
    public async Task ScanAsync_CleanTarArchive_ReturnsCleanAndScansEveryEntry()
    {
        string archivePath = Path.Combine(_temp.Path, "clean.tar");
        TarBuilder.WriteTar(archivePath,
        [
            new TarBuilder.Entry { Name = "a.txt", Content = Encoding.ASCII.GetBytes("hello") },
            new TarBuilder.Entry { Name = "sub/b.txt", Content = Encoding.ASCII.GetBytes("world") },
        ]);

        var scanner = new FakeAmsiScanner();
        var service = CreateService(scanner);

        var result = await service.ScanAsync(new AntivirusScanOptions { ArchivePaths = [archivePath] });

        result.OverallVerdict.Should().Be(ThreatVerdict.Clean);
        result.Findings.Should().HaveCount(2);
        result.Findings.Should().OnlyContain(f => f.Verdict == ThreatVerdict.Clean);
        scanner.ScannedContentNames.Should().Contain(["a.txt", "sub/b.txt"]);
        scanner.Disposed.Should().BeTrue();
    }

    [Integration]
    public async Task ScanAsync_TarArchiveWithDetectedEntry_ReturnsThreatDetectedAndIdentifiesEntry()
    {
        string archivePath = Path.Combine(_temp.Path, "infected.tar");
        TarBuilder.WriteTar(archivePath,
        [
            new TarBuilder.Entry { Name = "clean.txt", Content = Encoding.ASCII.GetBytes("fine") },
            new TarBuilder.Entry { Name = "bad.txt", Content = Encoding.ASCII.GetBytes("eicar-like") },
        ]);

        var scanner = new FakeAmsiScanner();
        scanner.DetectedContentNames.Add("bad.txt");
        var service = CreateService(scanner);

        var result = await service.ScanAsync(new AntivirusScanOptions { ArchivePaths = [archivePath] });

        result.OverallVerdict.Should().Be(ThreatVerdict.ThreatDetected);
        result.Findings.Should().ContainSingle(f => f.EntryPath == "bad.txt" && f.Verdict == ThreatVerdict.ThreatDetected);
        result.Findings.Should().ContainSingle(f => f.EntryPath == "clean.txt" && f.Verdict == ThreatVerdict.Clean);
    }

    [Integration]
    public async Task ScanAsync_SelectedEntryPathsSubset_OnlyScansSelectedEntries()
    {
        string archivePath = Path.Combine(_temp.Path, "multi.tar");
        TarBuilder.WriteTar(archivePath,
        [
            new TarBuilder.Entry { Name = "a.txt", Content = Encoding.ASCII.GetBytes("1") },
            new TarBuilder.Entry { Name = "b.txt", Content = Encoding.ASCII.GetBytes("2") },
            new TarBuilder.Entry { Name = "c.txt", Content = Encoding.ASCII.GetBytes("3") },
        ]);

        var scanner = new FakeAmsiScanner();
        var service = CreateService(scanner);

        var result = await service.ScanAsync(new AntivirusScanOptions
        {
            ArchivePaths = [archivePath],
            SelectedEntryPaths = ["b.txt"],
        });

        result.Findings.Should().ContainSingle();
        result.Findings[0].EntryPath.Should().Be("b.txt");
        scanner.ScannedContentNames.Should().ContainSingle().Which.Should().Be("b.txt");
    }

    // T-F49's whole-archive pre-scan (symlink/traversal rejection) applies identically to the scan
    // path — reused directly (ScanForUnsafeEntriesAsync bumped to internal for exactly this), not
    // re-implemented. A rejected archive must become Inconclusive, never an unhandled throw.
    [Integration]
    public async Task ScanAsync_ArchiveWithSymlinkEntry_ReturnsInconclusiveInsteadOfThrowing()
    {
        string archivePath = Path.Combine(_temp.Path, "symlink.tar");
        TarBuilder.WriteTar(archivePath,
        [
            new TarBuilder.Entry { Name = "escape", LinkName = "../../outside.txt", TypeFlag = '2' },
        ]);

        var scanner = new FakeAmsiScanner();
        var service = CreateService(scanner);

        var result = await service.ScanAsync(new AntivirusScanOptions { ArchivePaths = [archivePath] });

        result.OverallVerdict.Should().Be(ThreatVerdict.Inconclusive);
        result.Findings.Should().ContainSingle(f => f.ArchivePath == archivePath && f.EntryPath == null);
        scanner.ScannedContentNames.Should().BeEmpty();
    }
}
