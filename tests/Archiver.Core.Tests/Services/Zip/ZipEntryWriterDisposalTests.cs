using Archiver.Core.Services.Zip;
using FluentAssertions;

namespace Archiver.Core.Tests.Services.Zip;

// T-F500: a failure while the archive is finished (central directory, flush) used to leave the
// output open; with FileShare.None the caller's cleanup then could not delete the temp file.
public sealed class ZipEntryWriterDisposalTests
{
    [Fact]
    public async Task DisposeAsync_FlushFails_StillClosesTheOutputAndReportsTheFailure()
    {
        var output = new FailingFlushStream();
        var writer = new ZipEntryWriter(output);

        Func<Task> dispose = async () => await writer.DisposeAsync();

        await dispose.Should().ThrowAsync<IOException>().WithMessage("*disk full*");
        output.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task DisposeAsync_TheFileItWrote_CanBeDeletedAfterAFailure()
    {
        string path = Path.Combine(Path.GetTempPath(), "pakko-tf500-" + Guid.NewGuid().ToString("N") + ".zip");
        var file = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        var writer = new ZipEntryWriter(new FailingFlushStream(file));

        try
        {
            Func<Task> dispose = async () => await writer.DisposeAsync();
            await dispose.Should().ThrowAsync<IOException>();

            Action delete = () => File.Delete(path);
            delete.Should().NotThrow("the writer closed its output");
            File.Exists(path).Should().BeFalse();
        }
        finally
        {
            await file.DisposeAsync();
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private sealed class FailingFlushStream(Stream? inner = null) : Stream
    {
        private readonly Stream _inner = inner ?? new MemoryStream();

        public bool Disposed { get; private set; }

        public override bool CanRead => false;
        public override bool CanSeek => true;
        public override bool CanWrite => true;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public override void Flush() => throw new IOException("disk full");
        public override Task FlushAsync(CancellationToken cancellationToken) => throw new IOException("disk full");
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Disposed = true;
                _inner.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
