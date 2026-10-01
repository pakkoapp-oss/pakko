using System.Text;
using Archiver.Core.Services.Sandbox;
using FluentAssertions;

namespace Archiver.Core.Tests.Services.Sandbox;

// T-F305: whether every name in an uncompressed tar's headers is valid UTF-8 (7-Zip's rule for a
// header with no charset), read from the header bytes — libarchive words a GNU-magic header's
// invalid UTF-8 the same way as a valid UTF-8 name the code page cannot show.
public sealed class TarHeaderNamesTests
{
    private static readonly byte[] Utf8Name = Encoding.UTF8.GetBytes("Док.txt");
    private static readonly byte[] OemName = [0x84, 0xAE, 0xAA, (byte)'.', (byte)'t', (byte)'x', (byte)'t']; // "Док.txt" in cp866

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Utf8Name_True(bool gnu)
        => Walk(Tar(Entry(Utf8Name, gnu: gnu))).Should().BeTrue();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OemName_False(bool gnu)
        => Walk(Tar(Entry(OemName, gnu: gnu))).Should().BeFalse();

    [Fact]
    public void AsciiOnly_True()
        => Walk(Tar(Entry("a.txt"u8.ToArray(), content: [1, 2, 3]), Entry("dir/"u8.ToArray(), type: '5'))).Should().BeTrue();

    [Fact]
    public void OemNameAfterAnEntryWithContent_False()
        => Walk(Tar(Entry("a.txt"u8.ToArray(), content: new byte[700]), Entry(OemName))).Should().BeFalse();

    [Fact]
    public void PosixPrefixField_IsAName()
        => Walk(Tar(Entry("a.txt"u8.ToArray(), prefix: OemName))).Should().BeFalse();

    // In a GNU header offset 345 holds atime/ctime/sparse data, not a name prefix.
    [Fact]
    public void GnuHeader_BytesAtPrefixOffset_AreNotAName()
        => Walk(Tar(Entry("a.txt"u8.ToArray(), gnu: true, prefix: OemName))).Should().BeTrue();

    // 7-Zip and GNU tar cut the next header's name at 100 bytes, which can split a UTF-8 sequence.
    [Fact]
    public void LongNameRecord_ValidUtf8_TruncatedCopyInNextHeaderIsIgnored()
    {
        byte[] longName = Encoding.UTF8.GetBytes(new string('Д', 60) + ".txt"); // 124 bytes
        byte[] cut = longName[..99]; // ends inside a two-byte sequence
        Walk(Tar(Entry("././@LongLink"u8.ToArray(), type: 'L', gnu: true, content: [.. longName, 0]), Entry(cut, gnu: true)))
            .Should().BeTrue();
    }

    [Fact]
    public void LongNameRecord_Oem_False()
        => Walk(Tar(Entry("././@LongLink"u8.ToArray(), type: 'L', gnu: true, content: [.. OemName, 0]), Entry("x"u8.ToArray(), gnu: true)))
            .Should().BeFalse();

    [Fact]
    public void PaxRecord_NextHeaderNameIsIgnored()
        => Walk(Tar(Entry("PaxHeader"u8.ToArray(), type: 'x', content: "20 path=a/Dok.txt\n"u8.ToArray()), Entry(OemName)))
            .Should().BeTrue();

    [Fact]
    public void PaxRecord_OnlyTheNextHeaderIsIgnored()
        => Walk(Tar(Entry("PaxHeader"u8.ToArray(), type: 'x', content: "12 path=a.t\n"u8.ToArray()), Entry("a.t"u8.ToArray()), Entry(OemName)))
            .Should().BeFalse();

    [Fact]
    public void AfterTheEndOfArchiveBlock_NothingIsRead()
        => Walk([.. Tar(Entry(Utf8Name)), .. Entry(OemName)]).Should().BeTrue();

    [Fact]
    public void NoEndOfArchiveBlocks_StillDecided()
        => Walk(Entry(OemName)).Should().BeFalse();

    [Fact]
    public void BadChecksum_Unknown()
    {
        byte[] tar = Tar(Entry(OemName));
        tar[0] ^= 0x01;
        Walk(tar).Should().BeNull();
    }

    [Fact]
    public void BadChecksumOnALaterHeader_Unknown()
    {
        byte[] tar = Tar(Entry(Utf8Name), Entry(OemName));
        tar[512] ^= 0x01;
        Walk(tar).Should().BeNull();
    }

    [Fact]
    public void TruncatedHeader_Unknown()
        => Walk(Tar(Entry(Utf8Name))[..300]).Should().BeNull();

    [Fact]
    public void Base256Size_Unknown()
    {
        byte[] header = Entry(Utf8Name);
        header[124] = 0x80;
        Walk(Tar(Rechecksum(header))).Should().BeNull();
    }

    // Content running past the end of the file: tar.exe reports the truncation itself.
    [Fact]
    public void MaximalSizeField_EndsTheWalk()
    {
        byte[] header = Entry(OemName);
        Ascii(header, 124, "777777777777");
        Walk(Tar(Rechecksum(header))).Should().BeFalse();
    }

    [Fact]
    public void NonOctalSize_Unknown()
    {
        byte[] header = Entry(Utf8Name);
        Encoding.ASCII.GetBytes("0000000009x\0").CopyTo(header.AsSpan(124));
        Walk(Tar(Rechecksum(header))).Should().BeNull();
    }

    [Fact]
    public void HugeLongNameRecord_Unknown()
        => Walk(Tar(Entry("././@LongLink"u8.ToArray(), type: 'L', gnu: true, content: new byte[70_000]))).Should().BeNull();

    [Fact]
    public void GnuSparseEntry_Unknown()
        => Walk(Tar(Entry(Utf8Name, type: 'S', gnu: true))).Should().BeNull();

    [Theory]
    [InlineData(new byte[] { 0x1F, 0x8B, 0x08, 0x00 })] // gzip
    [InlineData(new byte[] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C })] // 7z
    [InlineData(new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00 })] // RAR5
    [InlineData(new byte[0])]
    public void NotAnUncompressedTar_Unknown(byte[] start)
    {
        byte[] file = new byte[Math.Max(start.Length, 2048)];
        start.CopyTo(file, 0);
        Walk(start.Length == 0 ? [] : file).Should().BeNull();
    }

    // A pre-POSIX (v7) header carries no magic; left to libarchive's own wording.
    [Fact]
    public void V7HeaderWithoutMagic_Unknown()
    {
        byte[] header = Entry(OemName);
        Array.Clear(header, 257, 8);
        Walk(Tar(Rechecksum(header))).Should().BeNull();
    }

    [Fact]
    public void Cancelled_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        byte[] tar = Tar(Entry(Utf8Name));
        Action act = () => TarHeaderNames.AreAllUtf8((buffer, offset) => Read(tar, buffer, offset), cts.Token);
        act.Should().Throw<OperationCanceledException>();
    }

    // T-F310: the same walk over a gzip-compressed tar, decompressed in-process as a forward stream.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Gzip_Utf8Name_True(bool gnu)
        => WalkGzip(Gzip(Tar(Entry(Utf8Name, gnu: gnu)))).Should().BeTrue();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Gzip_OemName_False(bool gnu)
        => WalkGzip(Gzip(Tar(Entry(OemName, gnu: gnu)))).Should().BeFalse();

    [Fact]
    public void Gzip_OemNameAfterLargeContent_False()
        => WalkGzip(Gzip(Tar(Entry("a.bin"u8.ToArray(), content: new byte[300_000]), Entry(OemName, gnu: true)))).Should().BeFalse();

    [Fact]
    public void Gzip_LongNameRecord_Oem_False()
        => WalkGzip(Gzip(Tar(Entry("././@LongLink"u8.ToArray(), type: 'L', gnu: true, content: [.. OemName, 0]), Entry("x"u8.ToArray(), gnu: true))))
            .Should().BeFalse();

    // The OEM name lies past the limit: undecided, never "all UTF-8".
    [Fact]
    public void Gzip_DecompressedSizeOverTheLimit_Unknown()
        => WalkGzip(Gzip(Tar(Entry("a.bin"u8.ToArray(), content: new byte[300_000]), Entry(OemName, gnu: true))), maxDecompressedBytes: 100_000)
            .Should().BeNull();

    [Fact]
    public void Gzip_CorruptStream_Unknown()
    {
        byte[] gzip = Gzip(Tar(Entry("a.bin"u8.ToArray(), content: RandomBytes(50_000)), Entry(OemName, gnu: true)));
        Array.Fill(gzip, (byte)0xFF, 2000, 2000);
        WalkGzip(gzip).Should().BeNull();
    }

    [Fact]
    public void Gzip_NotATarInside_Unknown()
        => WalkGzip(Gzip(RandomBytes(4096))).Should().BeNull();

    [Fact]
    public void Gzip_Cancelled_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        byte[] gzip = Gzip(Tar(Entry(Utf8Name)));
        Action act = () => TarHeaderNames.AreAllUtf8InGzip(new MemoryStream(gzip), long.MaxValue, cts.Token);
        act.Should().Throw<OperationCanceledException>();
    }

    private static bool? WalkGzip(byte[] gzip, long maxDecompressedBytes = long.MaxValue)
        => TarHeaderNames.AreAllUtf8InGzip(new MemoryStream(gzip), maxDecompressedBytes, CancellationToken.None);

    private static byte[] Gzip(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new System.IO.Compression.GZipStream(output, System.IO.Compression.CompressionLevel.Fastest))
            gzip.Write(data);
        return output.ToArray();
    }

    private static byte[] RandomBytes(int count)
    {
        byte[] bytes = new byte[count];
        new Random(310).NextBytes(bytes);
        return bytes;
    }

    private static bool? Walk(byte[] tar)
        => TarHeaderNames.AreAllUtf8((buffer, offset) => Read(tar, buffer, offset), CancellationToken.None);

    private static int Read(byte[] source, Span<byte> buffer, long offset)
    {
        if (offset >= source.Length)
            return 0;
        int n = (int)Math.Min(buffer.Length, source.Length - offset);
        source.AsSpan((int)offset, n).CopyTo(buffer);
        return n;
    }

    private static byte[] Tar(params byte[][] entries) => [.. entries.SelectMany(e => e), .. new byte[1024]];

    // One header followed by its content padded to 512 bytes.
    private static byte[] Entry(byte[] name, char type = '0', bool gnu = false, byte[]? content = null, byte[]? prefix = null)
    {
        content ??= [];
        byte[] header = new byte[512];
        name.AsSpan(0, Math.Min(100, name.Length)).CopyTo(header);
        Ascii(header, 100, "0000644\0");
        Ascii(header, 124, Convert.ToString(content.Length, 8).PadLeft(11, '0') + "\0");
        Ascii(header, 136, "00000000000\0");
        header[156] = (byte)type;
        Ascii(header, 257, gnu ? "ustar  \0" : "ustar\0" + "00");
        prefix?.AsSpan(0, Math.Min(155, prefix.Length)).CopyTo(header.AsSpan(345));
        Rechecksum(header);
        int padded = (content.Length + 511) / 512 * 512;
        byte[] entry = new byte[512 + padded];
        header.CopyTo(entry, 0);
        content.CopyTo(entry, 512);
        return entry;
    }

    private static byte[] Rechecksum(byte[] header)
    {
        Ascii(header, 148, "        ");
        int sum = 0;
        for (int i = 0; i < 512; i++)
            sum += header[i];
        Ascii(header, 148, Convert.ToString(sum, 8).PadLeft(6, '0') + "\0 ");
        return header;
    }

    private static void Ascii(byte[] buffer, int offset, string value) => Encoding.ASCII.GetBytes(value).CopyTo(buffer, offset);
}
