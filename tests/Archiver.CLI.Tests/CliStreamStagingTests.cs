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

    private static string CreateScratchDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "Archiver.CLI.Tests.Streaming", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

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
    }
}
