using System.Runtime.InteropServices;
using System.Text;
using FluentAssertions;

namespace Archiver.Shell.Tests;

// T-F235: the Explorer DLL writes the selection to Shell's stdin as UTF-16LE entries, each ended by
// a NUL, then one more NUL as the end marker (ShellExtUtils.cpp's BuildPathListPayload).
public sealed class StdinPathListTests
{
    private static MemoryStream Payload(string chars) =>
        new(MemoryMarshal.AsBytes(chars.AsSpan()).ToArray());

    private static MemoryStream PayloadOf(IEnumerable<string> paths) =>
        Payload(string.Concat(paths.Select(p => p + "\0")) + "\0");

    // --- Happy ---

    [Fact]
    public void Read_TwoPaths_ReturnsBothInOrder()
    {
        StdinPathListResult result = StdinPathList.Read(Payload("C:\\a.zip\0D:\\b c\\d.txt\0\0"));

        result.Error.Should().BeNull();
        result.Paths.Should().Equal("C:\\a.zip", "D:\\b c\\d.txt");
    }

    [Fact]
    public void Read_SameBytesAsTheNativeBuilder_ParsesIdentically()
    {
        // Byte-for-byte what BuildPathListPayload({ L"C:\\a", L"Z:\\" }) produces (see
        // ShellExtUtilsTests' BuildPathListPayload.FormatIsNulTerminatedEntriesPlusEndMarker).
        byte[] bytes =
        [
            (byte)'C', 0, (byte)':', 0, (byte)'\\', 0, (byte)'a', 0, 0, 0,
            (byte)'Z', 0, (byte)':', 0, (byte)'\\', 0, 0, 0,
            0, 0,
        ];

        StdinPathListResult result = StdinPathList.Read(new MemoryStream(bytes));

        result.Error.Should().BeNull();
        result.Paths.Should().Equal("C:\\a", "Z:\\");
    }

    // --- Security & Boundary ---

    [Fact]
    public void Read_TenThousandPaths_ReturnsEveryPath()
    {
        string[] paths = Enumerable.Range(0, 10_000)
            .Select(i => $"C:\\scratch\\many\\{i:D5}_{new string('x', 90)}.txt")
            .ToArray();

        StdinPathListResult result = StdinPathList.Read(PayloadOf(paths));

        result.Error.Should().BeNull();
        result.Paths.Should().Equal(paths);
    }

    [Fact]
    public void Read_QuotesSpacesTrailingBackslashAndUnicode_AreKeptVerbatim()
    {
        string[] paths = ["C:\\dir with \"quote\"\\x", "Z:\\", "C:\\\u0414\u0430\u043d\u0456\\\U0001F600.zip", "C:\\r\uFF02 --version \uFF02.txt"];

        StdinPathListResult result = StdinPathList.Read(PayloadOf(paths));

        result.Error.Should().BeNull();
        result.Paths.Should().Equal(paths);
    }

    [Fact]
    public void Read_LoneSurrogateInAName_IsKeptNotReplaced()
    {
        // NTFS names are UTF-16 code-unit sequences and may hold an unpaired surrogate; a decoder
        // that replaces it with U+FFFD would name a different (or no) file.
        string path = "C:\\bad\uD800name.txt";

        StdinPathListResult result = StdinPathList.Read(PayloadOf([path]));

        result.Error.Should().BeNull();
        result.Paths.Should().Equal(path);
    }

    [Fact]
    public void Read_OverTheSizeLimit_IsRejected()
    {
        var stream = new FakeEndlessStream(StdinPathList.MaxBytes + 2);

        StdinPathListResult result = StdinPathList.Read(stream);

        result.Error.Should().NotBeNull();
        result.Paths.Should().BeEmpty();
        stream.BytesRead.Should().BeLessThanOrEqualTo(StdinPathList.MaxBytes + 1 + 65536);
    }

    // --- Misuse ---

    [Fact]
    public void Read_NoStdin_IsRejected()
    {
        StdinPathListResult result = StdinPathList.Read(Stream.Null);

        result.Error.Should().NotBeNull();
        result.Paths.Should().BeEmpty();
    }

    [Fact]
    public void Read_EndMarkerOnly_IsRejectedAsEmptyList()
    {
        StdinPathListResult result = StdinPathList.Read(Payload("\0"));

        result.Error.Should().NotBeNull();
        result.Paths.Should().BeEmpty();
    }

    [Fact]
    public void Read_EmptyEntryBeforeTheEnd_IsRejected()
    {
        StdinPathListResult result = StdinPathList.Read(Payload("C:\\a\0\0C:\\b\0\0"));

        result.Error.Should().NotBeNull();
        result.Paths.Should().BeEmpty();
    }

    [Fact]
    public void Read_PlainUtf8Text_IsRejected()
    {
        StdinPathListResult result = StdinPathList.Read(new MemoryStream(Encoding.UTF8.GetBytes("C:\\a.zip\r\n")));

        result.Error.Should().NotBeNull();
        result.Paths.Should().BeEmpty();
    }

    // --- Error: the writer stopped early ---

    [Fact]
    public void Read_StreamEndsAfterACompleteEntryWithoutEndMarker_IsRejected()
    {
        // Exactly what a write that failed between two entries looks like: every entry so far is
        // complete, so a plain split-on-NUL reader would run on part of the selection.
        StdinPathListResult result = StdinPathList.Read(Payload("C:\\a\0C:\\b\0"));

        result.Error.Should().NotBeNull();
        result.Paths.Should().BeEmpty();
    }

    [Fact]
    public void Read_StreamEndsInsideAPath_IsRejected()
    {
        StdinPathListResult result = StdinPathList.Read(Payload("C:\\a\0C:\\b"));

        result.Error.Should().NotBeNull();
        result.Paths.Should().BeEmpty();
    }

    [Fact]
    public void Read_OddByteCount_IsRejected()
    {
        byte[] bytes = [.. MemoryMarshal.AsBytes("C:\\a\0\0".AsSpan()).ToArray(), 0x41];

        StdinPathListResult result = StdinPathList.Read(new MemoryStream(bytes));

        result.Error.Should().NotBeNull();
        result.Paths.Should().BeEmpty();
    }

    [Fact]
    public void Read_ReadFailure_IsRejectedNotThrown()
    {
        StdinPathListResult result = StdinPathList.Read(new FailingStream());

        result.Error.Should().NotBeNull();
        result.Paths.Should().BeEmpty();
    }

    private sealed class FakeEndlessStream(long length) : Stream
    {
        public long BytesRead { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = (int)Math.Min(count, length - BytesRead);
            Array.Fill(buffer, (byte)'a', offset, n);
            BytesRead += n;
            return n;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class FailingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("The pipe has been ended.");

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
