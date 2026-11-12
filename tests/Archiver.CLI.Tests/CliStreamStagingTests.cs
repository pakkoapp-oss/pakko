using Archiver.Core.IO;
using Archiver.CLI;
using FluentAssertions;

namespace Archiver.CLI.Tests;

public sealed class CliStreamStagingTests
{
    [Fact]
    public async Task StreamSingleFileAsync_ExactlyOneFile_CopiesBytesAndReturnsNull()
    {
        string dir = CreateScratchDir();
        byte[] content = [1, 2, 3, 4, 5];
        File.WriteAllBytes(Path.Combine(dir, "out.bin"), content);

        using var destination = new MemoryStream();
        string? error = await CliStreamStaging.StreamSingleFileAsync(dir, destination, CancellationToken.None);

        error.Should().BeNull();
        destination.ToArray().Should().Equal(content);
    }

    [Fact]
    public async Task StreamSingleFileAsync_ZeroFiles_ReturnsErrorNamingCount()
    {
        string dir = CreateScratchDir();

        using var destination = new MemoryStream();
        string? error = await CliStreamStaging.StreamSingleFileAsync(dir, destination, CancellationToken.None);

        error.Should().Contain("found 0");
    }

    [Fact]
    public async Task StreamSingleFileAsync_MultipleFiles_ReturnsErrorNamingCount()
    {
        string dir = CreateScratchDir();
        File.WriteAllText(Path.Combine(dir, "a.bin"), "a");
        File.WriteAllText(Path.Combine(dir, "b.bin"), "b");

        using var destination = new MemoryStream();
        string? error = await CliStreamStaging.StreamSingleFileAsync(dir, destination, CancellationToken.None);

        error.Should().Contain("found 2");
    }

    // T-F116: a downstream reader closing its end of the pipe (e.g. `pakko a -so ... | head`)
    // must be a clean, reported failure — not an unhandled exception. A real OS-pipe subprocess
    // test for this is racy (whether the writer notices the closed pipe depends on exact syscall
    // timing), so this exercises the same code path deterministically with a stream that throws
    // IOException on write, matching what a genuinely broken pipe raises.
    [Fact]
    public async Task StreamSingleFileAsync_DestinationThrowsIOException_ReturnsCleanErrorNoThrow()
    {
        string dir = CreateScratchDir();
        File.WriteAllBytes(Path.Combine(dir, "out.bin"), new byte[1024]);

        using var destination = new ThrowingStream();
        string? error = await CliStreamStaging.StreamSingleFileAsync(dir, destination, CancellationToken.None);

        error.Should().Contain("pipe");
    }

    // --- T-F244 item 4 / T-F263: -si staging owned from creation, swept after a dead process ---

    [Fact]
    public async Task StageStdinAsync_Success_CopiesBytesIntoAFolderNamedAfterThisProcess()
    {
        string root = CreateScratchDir();
        byte[] content = [7, 8, 9];

        using (CliStagingFolder folder = await CliStreamStaging.StageStdinAsync(root, new MemoryStream(content), CancellationToken.None))
        {
            Path.GetFileName(folder.Path).Should().StartWith(TempOwner.CurrentTag + "-");
            File.ReadAllBytes(Path.Combine(folder.Path, CliStreamStaging.StdinFileName)).Should().Equal(content);
        }

        Directory.GetFileSystemEntries(root).Should().BeEmpty("disposing the folder removes it");
    }

    // The staging path used to be known to the caller only after the copy finished, so a failure
    // mid-copy (disk full, broken pipe) leaked the folder with a partial archive in it.
    [Fact]
    public async Task StageStdinAsync_SourceFailsMidCopy_LeavesNothingBehind()
    {
        string root = CreateScratchDir();
        using var source = new FailingSource(onSecondRead: _ => throw new IOException("There is not enough space on the disk."));

        Func<Task> act = () => CliStreamStaging.StageStdinAsync(root, source, CancellationToken.None);

        await act.Should().ThrowAsync<IOException>();
        Directory.GetFileSystemEntries(root).Should().BeEmpty();
    }

    [Fact]
    public async Task StageStdinAsync_CancelledMidCopy_LeavesNothingBehind()
    {
        string root = CreateScratchDir();
        using var cancellation = new CancellationTokenSource();
        using var source = new FailingSource(onSecondRead: token =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
        });

        Func<Task> act = () => CliStreamStaging.StageStdinAsync(root, source, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        Directory.GetFileSystemEntries(root).Should().BeEmpty();
    }

    // T-F511 PR 2: a read-only file stopped the recursive delete and left the staging in %TEMP%.
    [Fact]
    public void Dispose_ReadOnlyFile_RemovesTheFolder()
    {
        string root = CreateScratchDir();
        var folder = CliStagingFolder.Create(root);
        string file = Path.Combine(folder.Path, "decrypted.txt");
        File.WriteAllText(file, "plaintext");
        File.SetAttributes(file, FileAttributes.ReadOnly);

        folder.Dispose();

        Directory.Exists(folder.Path).Should().BeFalse();
    }

    // T-F511 PR 2: the sweep is TempOwner's. A dead run's folder goes, in this version's name and in
    // v1.10's "<pid>-<guid>"; a live run's stays; a name it cannot read goes only once a day old.
    [Fact]
    public void SweepStaleInTemp_DeadRunsGo_LiveAndUnreadableStay()
    {
        string root = CreateScratchDir();
        string machine = TempOwner.CurrentTag.Split('-')[0];
        string dead = Directory.CreateDirectory(Path.Combine(root, $"{machine}-{int.MaxValue}-1-{Guid.NewGuid():N}")).FullName;
        File.WriteAllText(Path.Combine(dead, "decrypted.txt"), "plaintext left by a killed x -so");
        string legacyDead = Directory.CreateDirectory(Path.Combine(root, $"{int.MaxValue}-{Guid.NewGuid():N}")).FullName;
        string live = CliStagingFolder.Create(root).Path;
        string other = Directory.CreateDirectory(Path.Combine(root, "notes")).FullName;

        TempOwner.SweepStaleInTemp(root, "", TimeSpan.FromDays(1));

        Directory.Exists(dead).Should().BeFalse();
        Directory.Exists(legacyDead).Should().BeFalse();
        Directory.Exists(live).Should().BeTrue();
        Directory.Exists(other).Should().BeTrue();
    }

    [Fact]
    public void SweepAbandoned_RunsOverBothRootsWithoutThrowing()
    {
        Action act = CliStreamStaging.SweepAbandoned;

        act.Should().NotThrow();
    }

    private static string CreateScratchDir() => CliTestTemp.NewDir();

    private sealed class ThrowingStream : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new IOException("The pipe has been ended.");
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            throw new IOException("The pipe has been ended.");
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            throw new IOException("The pipe has been ended.");
    }

    // A readable source: the first read returns data (so the staged file exists), the second
    // runs onSecondRead, which throws.
    private sealed class FailingSource(Action<CancellationToken> onSecondRead) : Stream
    {
        private int _reads;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer) => Next(buffer, CancellationToken.None);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Next(buffer.Span, cancellationToken));
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private int Next(Span<byte> buffer, CancellationToken token)
        {
            if (++_reads == 1)
            {
                buffer[0] = 42;
                return 1;
            }
            onSecondRead(token);
            return 0;
        }
    }
}
