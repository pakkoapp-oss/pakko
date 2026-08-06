using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F143: TarSandboxedService.PollExtractionProgressAsync / ComputeDirectoryStateSnapshot
// (T-F142's real byte-progress-poll implementation) had no direct tests -- CLAUDE.md's T-F142
// entry only flagged the visible *rendering* as needing an on-device look, but the underlying
// math (monotonic clamping, the 94% extraction-phase ceiling, most-recent-file detection) is
// independently unit-testable without tar.exe or real subprocess timing, since
// PollExtractionProgressAsync already takes its "extraction" Task as a plain parameter. Bumped
// from private to internal static for this (T-F94/T-F114 precedent).
public sealed class TarSandboxedServiceProgressPollingTests
{
    // System.Progress<T> posts its callback via SynchronizationContext/ThreadPool, which would
    // race against this test's own assertions after the polling loop completes.
    private sealed class SynchronousProgress<T>(Action<T> onReport) : IProgress<T>
    {
        public void Report(T value) => onReport(value);
    }

    [Fact]
    public void ComputeDirectoryStateSnapshot_EmptyDirectory_ReturnsZeroAndNoFile()
    {
        using var temp = new TempDirectory();

        var (totalBytes, mostRecentFile) = TarSandboxedService.ComputeDirectoryStateSnapshot(temp.Path);

        totalBytes.Should().Be(0);
        mostRecentFile.Should().BeNull();
    }

    [Fact]
    public void ComputeDirectoryStateSnapshot_FilesAcrossSubdirectories_SumsAllBytes()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("a.txt", "12345");
        Directory.CreateDirectory(Path.Combine(temp.Path, "sub"));
        File.WriteAllText(Path.Combine(temp.Path, "sub", "b.txt"), "1234567890");

        var (totalBytes, _) = TarSandboxedService.ComputeDirectoryStateSnapshot(temp.Path);

        totalBytes.Should().Be(5 + 10);
    }

    [Fact]
    public void ComputeDirectoryStateSnapshot_MultipleFiles_ReturnsMostRecentlyWrittenAsCurrentFile()
    {
        using var temp = new TempDirectory();
        string older = temp.CreateFile("older.txt", "x");
        string newer = temp.CreateFile("newer.txt", "y");
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddMinutes(-5));
        File.SetLastWriteTimeUtc(newer, DateTime.UtcNow);

        var (_, mostRecentFile) = TarSandboxedService.ComputeDirectoryStateSnapshot(temp.Path);

        mostRecentFile.Should().Be("newer.txt");
    }

    [Fact]
    public async Task PollExtractionProgressAsync_RunsUntilExtractionTaskCompletes_ReportsAtLeastOnce()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("partial.bin", new string('a', 40));
        var reports = new List<ProgressReport>();
        var progress = new SynchronousProgress<ProgressReport>(r => reports.Add(r));

        // Long enough to survive at least one 250ms poll tick, short enough to keep the test fast.
        var extractionTask = Task.Delay(400);

        await TarSandboxedService.PollExtractionProgressAsync(
            temp.Path, totalBytes: 100, progress, extractionTask, CancellationToken.None);

        reports.Should().NotBeEmpty();
        reports.Should().OnlyContain(r => r.TotalBytes == 100);
        reports.Should().OnlyContain(r => r.CurrentFile == "partial.bin");
    }

    [Fact]
    public async Task PollExtractionProgressAsync_PercentNeverExceedsExtractionPhaseCeiling()
    {
        using var temp = new TempDirectory();
        // More bytes on disk than totalBytes -- simulates the declared total being an
        // underestimate; the real clamp should still cap percent at 94, never reach 100.
        temp.CreateFile("overshoot.bin", new string('a', 500));
        var reports = new List<ProgressReport>();
        var progress = new SynchronousProgress<ProgressReport>(r => reports.Add(r));

        var extractionTask = Task.Delay(400);

        await TarSandboxedService.PollExtractionProgressAsync(
            temp.Path, totalBytes: 100, progress, extractionTask, CancellationToken.None);

        reports.Should().NotBeEmpty();
        reports.Should().OnlyContain(r => r.Percent <= 94);
    }

    [Fact]
    public async Task PollExtractionProgressAsync_ReportedPercentIsMonotonicNonDecreasing()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("growing.bin", new string('a', 10));
        var reports = new List<ProgressReport>();
        var progress = new SynchronousProgress<ProgressReport>(r => reports.Add(r));

        var extractionTask = Task.Delay(400);

        await TarSandboxedService.PollExtractionProgressAsync(
            temp.Path, totalBytes: 1000, progress, extractionTask, CancellationToken.None);

        reports.Select(r => r.Percent).Should().BeInAscendingOrder();
        reports.Select(r => r.BytesTransferred).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task PollExtractionProgressAsync_AlreadyCancelled_StopsWithoutReporting()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("a.bin", "data");
        var reports = new List<ProgressReport>();
        var progress = new SynchronousProgress<ProgressReport>(r => reports.Add(r));

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var extractionTask = Task.Delay(5000, CancellationToken.None);

        await TarSandboxedService.PollExtractionProgressAsync(
            temp.Path, totalBytes: 100, progress, extractionTask, cts.Token);

        reports.Should().BeEmpty();
    }
}
