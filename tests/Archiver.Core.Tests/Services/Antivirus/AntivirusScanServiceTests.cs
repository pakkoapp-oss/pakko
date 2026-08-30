using System.IO.Compression;
using System.Text;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Services.Antivirus;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services.Antivirus;

// Hand-rolled fake — no mocking library is used anywhere in this repo (matches ExtractionRouterTests'
// own convention). Records every ScanBuffer call so tests can assert both "was this entry scanned"
// and "with what content name/length", and flags specific content names as ThreatDetected so tests
// don't need a real AV or a real EICAR buffer to exercise the ThreatDetected branch.
// Not file-scoped (unlike ExtractionRouterTests' fakes) — this class is used as an explicit
// parameter type in CreateService's signature below, which file-local types cannot be (CS9051).
internal sealed class FakeAmsiScanner : IAmsiScanner
{
    public HashSet<string> DetectedContentNames { get; } = new(StringComparer.Ordinal);
    public List<(string ContentName, int Length)> Calls { get; } = [];
    public bool Disposed { get; private set; }

    public (ThreatVerdict Verdict, string? ThreatName) ScanBuffer(byte[] buffer, int length, string contentName)
    {
        Calls.Add((contentName, length));
        return DetectedContentNames.Contains(contentName)
            ? (ThreatVerdict.ThreatDetected, "Fake-Test-Threat")
            : (ThreatVerdict.Clean, null);
    }

    public void Dispose() => Disposed = true;
}

public sealed class AntivirusScanServiceTests : IDisposable
{
    // System.Progress<T> posts its callback via SynchronizationContext/ThreadPool, so it can fire
    // after an assertion already ran — a synchronous fake avoids that race entirely (same fix
    // AggregateProgressStreamTests already established for this exact issue).
    private sealed class SynchronousProgress<T>(Action<T> onReport) : IProgress<T>
    {
        public void Report(T value) => onReport(value);
    }

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static readonly TarCapabilities NoTarSupport = new();

    private string CreateZip(string name, params (string EntryName, string Content)[] entries)
    {
        string path = Path.Combine(_temp.Path, name);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entryName, content) in entries)
        {
            ZipArchiveEntry entry = archive.CreateEntry(entryName);
            using Stream stream = entry.Open();
            byte[] bytes = Encoding.UTF8.GetBytes(content);
            stream.Write(bytes, 0, bytes.Length);
        }
        return path;
    }

    private string WriteRar(string name)
    {
        string path = Path.Combine(_temp.Path, name);
        File.WriteAllBytes(path, [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00]);
        return path;
    }

    private static AntivirusScanService CreateService(
        FakeAmsiScanner scanner,
        TarCapabilities? tarCapabilities = null,
        GroupPolicyOptions? policy = null,
        bool providerRegistered = true)
        => new(tarCapabilities ?? NoTarSupport, policy, () => scanner, () => providerRegistered);

    [Fact]
    public async Task ScanAsync_CleanZipArchive_ReturnsClean()
    {
        string zip = CreateZip("clean.zip", ("a.txt", "hello"), ("b.txt", "world"));
        var scanner = new FakeAmsiScanner();
        var service = CreateService(scanner);

        var result = await service.ScanAsync(new AntivirusScanOptions { ArchivePaths = [zip] });

        result.OverallVerdict.Should().Be(ThreatVerdict.Clean);
        result.Findings.Should().HaveCount(2);
        result.Findings.Should().OnlyContain(f => f.Verdict == ThreatVerdict.Clean);
        scanner.Calls.Should().HaveCount(2);
        scanner.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task ScanAsync_ZipArchiveWithDetectedEntry_ReturnsThreatDetectedAndIdentifiesEntry()
    {
        string zip = CreateZip("infected.zip", ("clean.txt", "fine"), ("bad.txt", "eicar-like"));
        var scanner = new FakeAmsiScanner();
        scanner.DetectedContentNames.Add("bad.txt");
        var service = CreateService(scanner);

        var result = await service.ScanAsync(new AntivirusScanOptions { ArchivePaths = [zip] });

        result.OverallVerdict.Should().Be(ThreatVerdict.ThreatDetected);
        result.Findings.Should().ContainSingle(f => f.EntryPath == "bad.txt" && f.Verdict == ThreatVerdict.ThreatDetected);
        result.Findings.Should().ContainSingle(f => f.EntryPath == "clean.txt" && f.Verdict == ThreatVerdict.Clean);
    }

    [Fact]
    public async Task ScanAsync_SelectedEntryPathsSubset_OnlyScansSelectedEntries()
    {
        string zip = CreateZip("multi.zip", ("a.txt", "1"), ("b.txt", "2"), ("c.txt", "3"));
        var scanner = new FakeAmsiScanner();
        var service = CreateService(scanner);

        var result = await service.ScanAsync(new AntivirusScanOptions
        {
            ArchivePaths = [zip],
            SelectedEntryPaths = ["b.txt"],
        });

        result.Findings.Should().ContainSingle();
        result.Findings[0].EntryPath.Should().Be("b.txt");
        scanner.Calls.Should().ContainSingle(c => c.ContentName == "b.txt");
    }

    [Fact]
    public async Task ScanAsync_EntryLargerThanCap_ReturnsInconclusiveWithoutCallingScanner()
    {
        string zip = CreateZip("dummy.zip", ("placeholder.txt", "x"));
        // Overwrite with a real oversized entry rather than trying to synthesize AntivirusScanService's
        // internal constant from the test — write one entry whose declared Length exceeds the cap by
        // actually writing that many bytes (kept small enough not to slow the suite down: a 1-byte
        // over-cap write would work too, but a clearly-oversized value makes intent obvious).
        string oversizedZip = Path.Combine(_temp.Path, "oversized.zip");
        using (var archive = ZipFile.Open(oversizedZip, ZipArchiveMode.Create))
        {
            ZipArchiveEntry entry = archive.CreateEntry("huge.bin", CompressionLevel.NoCompression);
            using Stream stream = entry.Open();
            byte[] chunk = new byte[1024 * 1024];
            long remaining = AntivirusScanService.MaxScannableEntryBytes + 1;
            while (remaining > 0)
            {
                int toWrite = (int)Math.Min(chunk.Length, remaining);
                stream.Write(chunk, 0, toWrite);
                remaining -= toWrite;
            }
        }

        var scanner = new FakeAmsiScanner();
        var service = CreateService(scanner);

        var result = await service.ScanAsync(new AntivirusScanOptions { ArchivePaths = [oversizedZip] });

        result.OverallVerdict.Should().Be(ThreatVerdict.Inconclusive);
        result.Findings.Should().ContainSingle(f => f.EntryPath == "huge.bin"
            && f.Verdict == ThreatVerdict.Inconclusive
            && f.Reason!.Contains("larger than"));
        scanner.Calls.Should().BeEmpty("an oversized entry must never be buffered into memory for scanning");
    }

    // T-F186 (test-coverage audit): the boundary companion to the over-cap test above — proves an
    // entry exactly AT the cap (MaxScannableEntryBytes itself; the real check is a strict `>`) is
    // still scanned, not skipped, closing the other side of T-F151's 64->256 MiB cap raise.
    // Real-bytes-written, matching the existing over-cap test's own established cost/precedent
    // (not tagged Slow there either) rather than a synthetic size that wouldn't exercise the real
    // comparison meaningfully.
    [Fact]
    public async Task ScanAsync_EntryExactlyAtCap_IsScannedNotSkipped()
    {
        string atCapZip = Path.Combine(_temp.Path, "at-cap.zip");
        using (var archive = ZipFile.Open(atCapZip, ZipArchiveMode.Create))
        {
            ZipArchiveEntry entry = archive.CreateEntry("at-cap.bin", CompressionLevel.NoCompression);
            using Stream stream = entry.Open();
            byte[] chunk = new byte[1024 * 1024];
            long remaining = AntivirusScanService.MaxScannableEntryBytes;
            while (remaining > 0)
            {
                int toWrite = (int)Math.Min(chunk.Length, remaining);
                stream.Write(chunk, 0, toWrite);
                remaining -= toWrite;
            }
        }

        var scanner = new FakeAmsiScanner();
        var service = CreateService(scanner);

        var result = await service.ScanAsync(new AntivirusScanOptions { ArchivePaths = [atCapZip] });

        result.Findings.Should().ContainSingle(f => f.EntryPath == "at-cap.bin" && f.Verdict == ThreatVerdict.Clean);
        scanner.Calls.Should().ContainSingle(c => c.ContentName == "at-cap.bin"
            && c.Length == AntivirusScanService.MaxScannableEntryBytes,
            "an entry exactly at the cap must still be buffered and scanned, not skipped");
    }

    [Fact]
    public async Task ScanAsync_NoProviderRegistered_ReturnsInconclusiveForEveryArchiveWithoutScanning()
    {
        string zip = CreateZip("clean.zip", ("a.txt", "hello"));
        var scanner = new FakeAmsiScanner();
        var service = CreateService(scanner, providerRegistered: false);

        var result = await service.ScanAsync(new AntivirusScanOptions { ArchivePaths = [zip] });

        result.OverallVerdict.Should().Be(ThreatVerdict.Inconclusive);
        result.Findings.Should().ContainSingle(f => f.ArchivePath == zip
            && f.Verdict == ThreatVerdict.Inconclusive
            && f.Reason!.Contains("No antivirus"));
        scanner.Calls.Should().BeEmpty("no provider means AmsiScanBuffer must never be called at all");
    }

    [Fact]
    public async Task ScanAsync_BlockedFormatPolicy_ReturnsInconclusiveWithGroupPolicyReason()
    {
        string zip = CreateZip("blocked.zip", ("a.txt", "hello"));
        var scanner = new FakeAmsiScanner();
        var policy = new GroupPolicyOptions { BlockedFormats = ["zip"] };
        var service = CreateService(scanner, policy: policy);

        var result = await service.ScanAsync(new AntivirusScanOptions { ArchivePaths = [zip] });

        result.OverallVerdict.Should().Be(ThreatVerdict.Inconclusive);
        result.Findings.Should().ContainSingle(f => f.ArchivePath == zip && f.Reason!.Contains("Group Policy"));
        scanner.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ScanAsync_TarFormatWithoutCapabilities_ReturnsInconclusiveWithUnsupportedReason()
    {
        string rar = WriteRar("only.rar");
        var scanner = new FakeAmsiScanner();
        var service = CreateService(scanner, tarCapabilities: NoTarSupport);

        var result = await service.ScanAsync(new AntivirusScanOptions { ArchivePaths = [rar] });

        result.OverallVerdict.Should().Be(ThreatVerdict.Inconclusive);
        result.Findings.Should().ContainSingle(f => f.ArchivePath == rar && f.Reason!.Contains("RAR"));
        scanner.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ScanAsync_UnrecognizedFile_ReturnsInconclusiveInsteadOfThrowing()
    {
        string garbage = Path.Combine(_temp.Path, "not-an-archive.zip");
        File.WriteAllBytes(garbage, [1, 2, 3, 4, 5]);
        var scanner = new FakeAmsiScanner();
        var service = CreateService(scanner);

        var result = await service.ScanAsync(new AntivirusScanOptions { ArchivePaths = [garbage] });

        result.OverallVerdict.Should().Be(ThreatVerdict.Inconclusive);
        result.Findings.Should().ContainSingle(f => f.ArchivePath == garbage && f.EntryPath == null);
    }

    [Fact]
    public async Task ScanAsync_EmptyArchivePathsList_ReturnsCleanWithNoFindings()
    {
        var scanner = new FakeAmsiScanner();
        var service = CreateService(scanner);

        var result = await service.ScanAsync(new AntivirusScanOptions { ArchivePaths = [] });

        result.OverallVerdict.Should().Be(ThreatVerdict.Clean);
        result.Findings.Should().BeEmpty();
    }

    [Fact]
    public async Task ScanAsync_ReportsProgressAsArchivesComplete()
    {
        string zip1 = CreateZip("a.zip", ("x.txt", "1"));
        string zip2 = CreateZip("b.zip", ("y.txt", "2"));
        var scanner = new FakeAmsiScanner();
        var service = CreateService(scanner);
        var reports = new List<ProgressReport>();
        var progress = new SynchronousProgress<ProgressReport>(reports.Add);

        await service.ScanAsync(new AntivirusScanOptions { ArchivePaths = [zip1, zip2] }, progress);

        reports.Should().NotBeEmpty();
        reports[^1].Percent.Should().Be(100);
        reports.Select(r => r.Percent).Should().BeInAscendingOrder(
            "progress must never regress between archives in a multi-archive selection");
    }

    // Regression test for a real UX gap: progress used to be reported once per ARCHIVE, so a
    // single-archive scan (the common case — Explorer single selection, Archive Browser's button)
    // produced exactly one report at the very end (100%) and nothing while it was actually
    // running. Confirmed failing against a revert of the per-entry reportProgress plumbing before
    // being left passing, per this project's own regression-test discipline.
    [Fact]
    public async Task ScanAsync_SingleArchiveWithMultipleEntries_ReportsRealIntermediateProgress()
    {
        string zip = CreateZip("multi.zip", ("a.txt", "1"), ("b.txt", "2"), ("c.txt", "3"), ("d.txt", "4"));
        var scanner = new FakeAmsiScanner();
        var service = CreateService(scanner);
        var reports = new List<ProgressReport>();
        var progress = new SynchronousProgress<ProgressReport>(reports.Add);

        await service.ScanAsync(new AntivirusScanOptions { ArchivePaths = [zip] }, progress);

        reports.Count.Should().BeGreaterThan(1,
            "a 4-entry single-archive scan must report progress as each entry finishes, not just once at the end");
        reports.Should().Contain(r => r.Percent > 0 && r.Percent < 100,
            "at least one report must show real intermediate progress, not just a jump from nothing to 100%");
        reports[^1].Percent.Should().Be(100);
        reports.Select(r => r.Percent).Should().BeInAscendingOrder();
    }
}
