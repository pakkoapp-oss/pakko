using Archiver.Core.IO;
using Archiver.Core.Models;
using FluentAssertions;

namespace Archiver.Core.Tests.IO;

// T-F143: AggregateProgressStream previously had no direct tests -- only ever exercised
// indirectly via FileHashService's real folder-hashing path, which never touches the byte[]-based
// ReadAsync overload, the NotSupportedException members, or Dispose directly.
public sealed class AggregateProgressStreamTests
{
    // System.Progress<T> posts its callback via SynchronizationContext/ThreadPool, so it can fire
    // after the assertion below already ran -- a synchronous fake avoids that race entirely.
    private sealed class SynchronousProgress<T>(Action<T> onReport) : IProgress<T>
    {
        public void Report(T value) => onReport(value);
    }

    private static AggregateProgressTracker MakeTracker(long totalBytes, List<ProgressReport> reports) =>
        new(totalBytes, new SynchronousProgress<ProgressReport>(r => reports.Add(r)));

    [Fact]
    public void StreamCapabilities_ReadOnlyDelegatingToInner()
    {
        var inner = new MemoryStream(new byte[4]);
        var reports = new List<ProgressReport>();
        using var stream = new AggregateProgressStream(inner, MakeTracker(4, reports), currentFile: "a.bin");

        stream.CanRead.Should().Be(inner.CanRead);
        stream.CanWrite.Should().BeFalse();
        stream.CanSeek.Should().BeFalse();
        stream.Length.Should().Be(inner.Length);
    }

    [Fact]
    public void Position_Get_DelegatesToInner()
    {
        var inner = new MemoryStream(new byte[4]);
        inner.Position = 2;
        var reports = new List<ProgressReport>();
        using var stream = new AggregateProgressStream(inner, MakeTracker(4, reports), currentFile: null);

        stream.Position.Should().Be(2);
    }

    [Fact]
    public void Position_Set_ThrowsNotSupported()
    {
        var reports = new List<ProgressReport>();
        using var stream = new AggregateProgressStream(new MemoryStream(new byte[4]), MakeTracker(4, reports), currentFile: null);

        var act = () => stream.Position = 1;

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Seek_ThrowsNotSupported()
    {
        var reports = new List<ProgressReport>();
        using var stream = new AggregateProgressStream(new MemoryStream(new byte[4]), MakeTracker(4, reports), currentFile: null);

        var act = () => stream.Seek(0, SeekOrigin.Begin);

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void SetLength_ThrowsNotSupported()
    {
        var reports = new List<ProgressReport>();
        using var stream = new AggregateProgressStream(new MemoryStream(new byte[4]), MakeTracker(4, reports), currentFile: null);

        var act = () => stream.SetLength(10);

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Write_ThrowsNotSupported()
    {
        var reports = new List<ProgressReport>();
        using var stream = new AggregateProgressStream(new MemoryStream(new byte[4]), MakeTracker(4, reports), currentFile: null);

        var act = () => stream.Write([1, 2, 3], 0, 3);

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Flush_DelegatesToInner()
    {
        var inner = new MemoryStream(new byte[4]);
        var reports = new List<ProgressReport>();
        using var stream = new AggregateProgressStream(inner, MakeTracker(4, reports), currentFile: null);

        var act = () => stream.Flush();

        act.Should().NotThrow();
    }

    [Fact]
    public void Read_ByteArrayOverload_ReportsIntoSharedTracker()
    {
        var inner = new MemoryStream([1, 2, 3, 4]);
        var reports = new List<ProgressReport>();
        using var stream = new AggregateProgressStream(inner, MakeTracker(4, reports), currentFile: "file.bin");

        var buffer = new byte[4];
        int read = stream.Read(buffer, 0, 4);

        read.Should().Be(4);
        reports.Should().ContainSingle(r => r.Percent == 100 && r.CurrentFile == "file.bin");
    }

    [Fact]
    public async Task ReadAsync_ByteArrayOverload_ReportsIntoSharedTracker()
    {
        var inner = new MemoryStream([1, 2, 3, 4]);
        var reports = new List<ProgressReport>();
        using var stream = new AggregateProgressStream(inner, MakeTracker(4, reports), currentFile: "file.bin");

        var buffer = new byte[4];
#pragma warning disable CA1835 // deliberately exercises the legacy byte[] overload under test, not a perf-sensitive call site
        int read = await stream.ReadAsync(buffer, 0, 4, CancellationToken.None);
#pragma warning restore CA1835

        read.Should().Be(4);
        reports.Should().ContainSingle(r => r.Percent == 100 && r.CurrentFile == "file.bin");
    }

    [Fact]
    public async Task ReadAsync_MemoryOverload_ReportsIntoSharedTracker()
    {
        var inner = new MemoryStream([1, 2, 3, 4]);
        var reports = new List<ProgressReport>();
        using var stream = new AggregateProgressStream(inner, MakeTracker(4, reports), currentFile: "file.bin");

        var buffer = new byte[4];
        int read = await stream.ReadAsync(buffer.AsMemory());

        read.Should().Be(4);
        reports.Should().ContainSingle(r => r.Percent == 100 && r.CurrentFile == "file.bin");
    }

    [Fact]
    public void MultipleStreams_ShareOneTrackerAcrossFiles()
    {
        var reports = new List<ProgressReport>();
        var tracker = MakeTracker(8, reports);

        using (var s1 = new AggregateProgressStream(new MemoryStream([1, 2, 3, 4]), tracker, "a.bin"))
        {
            s1.Read(new byte[4], 0, 4);
        }
        using (var s2 = new AggregateProgressStream(new MemoryStream([5, 6, 7, 8]), tracker, "b.bin"))
        {
            s2.Read(new byte[4], 0, 4);
        }

        reports.Should().HaveCount(2);
        reports[0].Percent.Should().Be(50);
        reports[1].Percent.Should().Be(100);
    }

    [Fact]
    public void Dispose_DisposesInnerStream()
    {
        var inner = new MemoryStream(new byte[4]);
        var reports = new List<ProgressReport>();
        var stream = new AggregateProgressStream(inner, MakeTracker(4, reports), currentFile: null);

        stream.Dispose();

        var act = () => inner.ReadByte();
        act.Should().Throw<ObjectDisposedException>();
    }
}
