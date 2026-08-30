using System.IO.Compression;
using System.Runtime.Versioning;
using System.Text;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Services.Antivirus;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// Marks a test as requiring both the real system tar.exe (<see cref="IntegrationAttribute"/>'s
/// own check) and a real, working AMSI provider (the same probe-scan approach
/// Archiver.Core.Tests' own SkipIfAmsiScanUnavailableAttribute uses — duplicated here rather than
/// shared across test projects, matching this file's own FakeAmsiScanner precedent in
/// AntivirusScanServiceTarTests.cs). xUnit only allows one FactAttribute-derived attribute per
/// test method, so the two independent gates are combined into one.
/// </summary>
public sealed class SkipIfTarOrAmsiUnavailableAttribute : FactAttribute
{
    public SkipIfTarOrAmsiUnavailableAttribute()
    {
        if (!File.Exists(@"C:\Windows\System32\tar.exe"))
        {
            Skip = "tar.exe not present at C:\\Windows\\System32\\tar.exe";
            return;
        }

        // T-F177 follow-up: probing with an innocuous buffer only proves ScanBuffer doesn't
        // throw, not that it still actually detects anything — a live-but-degraded AMSI provider
        // (confirmed to happen on this machine mid-session, see docs/TASKS.md's T-F177 entry)
        // passes a non-throw probe yet silently reports Clean for real EICAR, turning this
        // gate into a false "available" signal instead of skipping cleanly. Probe with the real
        // EICAR string and require an actual ThreatDetected verdict.
        try
        {
            using AmsiScanner scanner = new("PakkoTests");
            byte[] eicar = System.Text.Encoding.ASCII.GetBytes(
                "X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*");
            var (verdict, _) = scanner.ScanBuffer(eicar, eicar.Length, "probe-eicar.txt");
            if (verdict != ThreatVerdict.ThreatDetected)
                Skip = $"AMSI is live but not currently detecting EICAR on this machine (probe verdict: {verdict})";
        }
        catch (InvalidOperationException ex)
        {
            Skip = $"AMSI scanning is not available on this machine: {ex.Message}";
        }
    }
}

/// <summary>
/// T-F177 (test-coverage audit): every existing ScanAsync_...DetectedEntry test (Zip and Tar,
/// across both Archiver.Core.Tests and this project) uses FakeAmsiScanner — real EICAR bytes were
/// only ever driven through AmsiScanner.ScanBuffer directly
/// (AmsiScannerTests.ScanBuffer_EicarTestString_ReturnsThreatDetected), never through
/// AntivirusScanService.ScanAsync's real orchestration (entry extraction/quarantine + buffer
/// scan) with the real scanner. These two tests close that gap. Environment-dependent by nature,
/// same as AmsiScannerTests — exercises whatever AV is actually registered as this machine's AMSI
/// provider. EICAR is built at runtime, never committed as a static literal (matches
/// AmsiScannerTests' own documented reasoning: Defender quarantines a committed EICAR fixture on
/// clone/build).
/// </summary>
// [SupportedOSPlatform("windows")] on the class itself, matching AmsiProviderCheckTests' own
// precedent — the real public AntivirusScanService constructor defaults to
// AmsiProviderCheck.IsAnyProviderRegistered, which the BCL's Microsoft.Win32.Registry call marks
// Windows-only; this whole test project only ever runs on Windows.
[SupportedOSPlatform("windows")]
[Collection("TarSandbox")]
public sealed class AntivirusScanServiceEicarTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static string BuildEicarString() =>
        "X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*";

    // Uses the real public constructor (real AmsiScanner + AmsiProviderCheck, the exact
    // production wiring) rather than the internal test-only overload other AntivirusScanService
    // tests use — that overload exists specifically to substitute a FakeAmsiScanner, which is the
    // one thing these two tests deliberately do NOT want.
    private static AntivirusScanService CreateRealService(TarCapabilities? tarCapabilities = null) =>
        new(tarCapabilities ?? new TarCapabilities());

    [SkipIfTarOrAmsiUnavailable]
    public async Task ScanAsync_RealEicarInZipArchive_ReturnsThreatDetected()
    {
        // The primary, reliable automated regression for this task: ZIP entries scan entirely
        // in-memory (no on-disk quarantine write for real-time AV to race against), so this is
        // deterministic given a real AMSI provider — unlike the Tar variant below.
        string archivePath = Path.Combine(_temp.Path, "eicar.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            ZipArchiveEntry cleanEntry = archive.CreateEntry("clean.txt");
            using (Stream s = cleanEntry.Open())
            {
                byte[] bytes = Encoding.ASCII.GetBytes("nothing to see here");
                s.Write(bytes, 0, bytes.Length);
            }

            ZipArchiveEntry eicarEntry = archive.CreateEntry("eicar.txt");
            using (Stream s = eicarEntry.Open())
            {
                byte[] bytes = Encoding.ASCII.GetBytes(BuildEicarString());
                s.Write(bytes, 0, bytes.Length);
            }
        }

        var service = CreateRealService();
        var result = await service.ScanAsync(new AntivirusScanOptions { ArchivePaths = [archivePath] });

        result.OverallVerdict.Should().Be(ThreatVerdict.ThreatDetected);
        result.Findings.Should().ContainSingle(f => f.EntryPath == "eicar.txt" && f.Verdict == ThreatVerdict.ThreatDetected);
        result.Findings.Should().ContainSingle(f => f.EntryPath == "clean.txt" && f.Verdict == ThreatVerdict.Clean);
    }

    [SkipIfTarOrAmsiUnavailable]
    public async Task ScanAsync_RealEicarInTarArchive_ReturnsThreatDetectedOrFixtureInterceptedByRealTimeAv()
    {
        // The Tar path writes real EICAR bytes into TarSandboxScope's on-disk quarantine directory
        // before AMSI ever runs — T-F146's own Phase 0 finding was that Defender's real-time
        // on-access scanner can independently intercept/remove such a file before the AMSI call
        // gets to it. Both ThreatDetected (AMSI won the race) and "the fixture file is gone/
        // unreadable by the time ScanAsync tried to read it" (real-time AV won the race first) are
        // treated as passing outcomes here — both prove the threat was actually caught by
        // something, just by a different real component. Only a silent Clean/Inconclusive verdict
        // for a real EICAR entry would indicate an actual regression.
        string archivePath = Path.Combine(_temp.Path, "eicar.tar");
        TarBuilder.WriteTar(archivePath,
        [
            new TarBuilder.Entry { Name = "clean.txt", Content = Encoding.ASCII.GetBytes("fine") },
            new TarBuilder.Entry { Name = "eicar.txt", Content = Encoding.ASCII.GetBytes(BuildEicarString()) },
        ]);

        var service = CreateRealService();

        ThreatScanResult result;
        try
        {
            result = await service.ScanAsync(new AntivirusScanOptions { ArchivePaths = [archivePath] });
        }
        catch (IOException)
        {
            // Real-time AV removed/locked the quarantined fixture before ScanAsync could read it
            // back — an accepted, documented outcome (see class doc comment above), not a failure.
            return;
        }

        if (result.OverallVerdict == ThreatVerdict.ThreatDetected)
        {
            result.Findings.Should().ContainSingle(f => f.EntryPath == "eicar.txt" && f.Verdict == ThreatVerdict.ThreatDetected);
        }
        else
        {
            // The only other accepted outcome: real-time AV won the race and the entry never made
            // it into the quarantine snapshot AMSI scanned — reflected here as Inconclusive rather
            // than a silent Clean, which would be the actual regression this test guards against.
            result.OverallVerdict.Should().Be(ThreatVerdict.Inconclusive,
                "a real EICAR entry must never be reported Clean — either AMSI catches it " +
                "(ThreatDetected) or real-time AV intercepts the fixture first (Inconclusive)");
        }
    }
}
