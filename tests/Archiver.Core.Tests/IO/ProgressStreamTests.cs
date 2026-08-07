using Archiver.Core.IO;
using Archiver.Core.Models;
using FluentAssertions;

namespace Archiver.Core.Tests.IO;

// T-F143: ProgressStream previously had no direct tests at all -- everything it does was only
// ever exercised indirectly through Stream.CopyToAsync's default Memory<byte>-based overload
// (via real ZipArchiveService/TarSandboxedService operations), leaving the legacy byte[]-based
// Read/ReadAsync/Write/WriteAsync overloads, the 2-arg constructor, and the NotSupportedException
// members entirely uncovered.
public sealed class ProgressStreamTests
{
    // System.Progress<T> posts its callback via SynchronizationContext/ThreadPool, so it can fire
    // after the assertion below already ran -- a synchronous fake avoids that race entirely.
    private sealed class SynchronousProgress<T>(Action<T> onReport) : IProgress<T>
    {
        public void Report(T value) => onReport(value);
    }

    [Fact]
    public void TwoArgConstructor_StartsWithNoPriorOffset_FirstByteReportsNonNegativePercent()
    {
        var inner = new MemoryStream(new byte[10]);
        var reports = new List<ProgressReport>();
        using var stream = new ProgressStream(inner, totalBytes: 10, new SynchronousProgress<ProgressReport>(r => reports.Add(r)));

        stream.Write([1, 2, 3], 0, 3);

        reports.Should().ContainSingle(r => r.Percent == 30);
    }

    [Fact]
    public void FourArgConstructor_WithStartOffsetAndCurrentFile_ResumesFromOffset()
    {
        var inner = new MemoryStream(new byte[10]);
        var reports = new List<ProgressReport>();
        using var stream = new ProgressStream(inner, totalBytes: 10, startOffset: 5,
            new SynchronousProgress<ProgressReport>(r => reports.Add(r)), currentFile: "resumed.bin");

        // Writing 1 more byte crosses from the 50% starting point to 60% -- a new percent, so it reports.
        stream.Write([9], 0, 1);

        reports.Should().ContainSingle();
        reports[0].Percent.Should().Be(60);
        reports[0].CurrentFile.Should().Be("resumed.bin");
    }

    [Fact]
    public void StreamCapabilities_DelegateToInnerExceptSeek()
    {
        var inner = new MemoryStream(new byte[4]);
        using var stream = new ProgressStream(inner, totalBytes: 4, new SynchronousProgress<ProgressReport>(_ => { }));

        stream.CanRead.Should().Be(inner.CanRead);
        stream.CanWrite.Should().Be(inner.CanWrite);
        stream.CanSeek.Should().BeFalse();
        stream.Length.Should().Be(inner.Length);
    }

    [Fact]
    public void Position_Get_DelegatesToInner()
    {
        var inner = new MemoryStream(new byte[4]);
        inner.Position = 2;
        using var stream = new ProgressStream(inner, totalBytes: 4, new SynchronousProgress<ProgressReport>(_ => { }));

        stream.Position.Should().Be(2);
    }

    [Fact]
    public void Position_Set_ThrowsNotSupported()
    {
        using var stream = new ProgressStream(new MemoryStream(new byte[4]), totalBytes: 4, new SynchronousProgress<ProgressReport>(_ => { }));

        var act = () => stream.Position = 1;

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Seek_ThrowsNotSupported()
    {
        using var stream = new ProgressStream(new MemoryStream(new byte[4]), totalBytes: 4, new SynchronousProgress<ProgressReport>(_ => { }));

        var act = () => stream.Seek(0, SeekOrigin.Begin);

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void SetLength_ThrowsNotSupported()
    {
        using var stream = new ProgressStream(new MemoryStream(new byte[4]), totalBytes: 4, new SynchronousProgress<ProgressReport>(_ => { }));

        var act = () => stream.SetLength(10);

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Flush_DelegatesToInner()
    {
        var inner = new MemoryStream(new byte[4]);
        using var stream = new ProgressStream(inner, totalBytes: 4, new SynchronousProgress<ProgressReport>(_ => { }));

        var act = () => stream.Flush();

        act.Should().NotThrow();
    }

    [Fact]
    public void Read_ByteArrayOverload_ReportsProgress()
    {
        var inner = new MemoryStream([1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
        var reports = new List<ProgressReport>();
        using var stream = new ProgressStream(inner, totalBytes: 10, new SynchronousProgress<ProgressReport>(r => reports.Add(r)));

        var buffer = new byte[10];
        int read = stream.Read(buffer, 0, 10);

        read.Should().Be(10);
        reports.Should().ContainSingle(r => r.Percent == 100);
    }

    [Fact]
    public async Task ReadAsync_ByteArrayOverload_ReportsProgress()
    {
        var inner = new MemoryStream([1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
        var reports = new List<ProgressReport>();
        using var stream = new ProgressStream(inner, totalBytes: 10, new SynchronousProgress<ProgressReport>(r => reports.Add(r)));

        var buffer = new byte[10];
        int read = await stream.ReadAsync(buffer, 0, 10, CancellationToken.None); // NOSONAR: CA1835 — deliberately exercises the legacy byte[] overload under test, not a perf-sensitive call site

        read.Should().Be(10);
        reports.Should().ContainSingle(r => r.Percent == 100);
    }

    [Fact]
    public async Task ReadAsync_MemoryOverload_ReportsProgress()
    {
        var inner = new MemoryStream([1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
        var reports = new List<ProgressReport>();
        using var stream = new ProgressStream(inner, totalBytes: 10, new SynchronousProgress<ProgressReport>(r => reports.Add(r)));

        var buffer = new byte[10];
        int read = await stream.ReadAsync(buffer.AsMemory());

        read.Should().Be(10);
        reports.Should().ContainSingle(r => r.Percent == 100);
    }

    [Fact]
    public void Write_ByteArrayOverload_ReportsProgress()
    {
        var inner = new MemoryStream();
        var reports = new List<ProgressReport>();
        using var stream = new ProgressStream(inner, totalBytes: 5, new SynchronousProgress<ProgressReport>(r => reports.Add(r)));

        stream.Write([1, 2, 3, 4, 5], 0, 5);

        reports.Should().ContainSingle(r => r.Percent == 100);
    }

    [Fact]
    public async Task WriteAsync_ByteArrayOverload_ReportsProgress()
    {
        var inner = new MemoryStream();
        var reports = new List<ProgressReport>();
        using var stream = new ProgressStream(inner, totalBytes: 5, new SynchronousProgress<ProgressReport>(r => reports.Add(r)));

        await stream.WriteAsync([1, 2, 3, 4, 5], 0, 5, CancellationToken.None); // NOSONAR: CA1835 — deliberately exercises the legacy byte[] overload under test, not a perf-sensitive call site

        reports.Should().ContainSingle(r => r.Percent == 100);
    }

    [Fact]
    public async Task WriteAsync_ReadOnlyMemoryOverload_ReportsProgress()
    {
        var inner = new MemoryStream();
        var reports = new List<ProgressReport>();
        using var stream = new ProgressStream(inner, totalBytes: 5, new SynchronousProgress<ProgressReport>(r => reports.Add(r)));

        await stream.WriteAsync(new byte[] { 1, 2, 3, 4, 5 }.AsMemory());

        reports.Should().ContainSingle(r => r.Percent == 100);
    }

    [Fact]
    public void Report_ZeroTotalBytes_NeverReports()
    {
        var inner = new MemoryStream();
        var reports = new List<ProgressReport>();
        using var stream = new ProgressStream(inner, totalBytes: 0, new SynchronousProgress<ProgressReport>(r => reports.Add(r)));

        stream.Write([1, 2, 3], 0, 3);

        reports.Should().BeEmpty();
    }

    [Fact]
    public void Report_SamePercentTwice_ReportsOnlyOnce()
    {
        var inner = new MemoryStream();
        var reports = new List<ProgressReport>();
        // A huge total means writing a handful of bytes never moves the rounded percent past 0.
        using var stream = new ProgressStream(inner, totalBytes: 1_000_000, new SynchronousProgress<ProgressReport>(r => reports.Add(r)));

        stream.Write([1], 0, 1);
        stream.Write([2], 0, 1);

        // The first write still reports once (0% is a new percent vs. the -1 initial sentinel);
        // the second write stays at 0% too, so it must not report again.
        reports.Should().ContainSingle();
    }

    [Fact]
    public void Dispose_DisposesInnerStream()
    {
        var inner = new MemoryStream();
        var stream = new ProgressStream(inner, totalBytes: 4, new SynchronousProgress<ProgressReport>(_ => { }));

        stream.Dispose();

        var act = () => inner.WriteByte(1);
        act.Should().Throw<ObjectDisposedException>();
    }
}
