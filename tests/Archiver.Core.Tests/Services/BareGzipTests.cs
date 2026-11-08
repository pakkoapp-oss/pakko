using System.Buffers.Binary;
using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using Archiver.Core.IO;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

/// <summary>
/// T-F413: a gzip stream of one file. What is one (not a tar or another archive inside), the name
/// of the file inside (7-Zip's rule), and reading it with every member's trailer checked - .NET's
/// GZipStream ends without a word on a stream cut before its trailer or followed by other bytes.
/// </summary>
public sealed class BareGzipTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    // One gzip member, built by hand so every header field and trailer byte can be set.
    internal static byte[] Member(byte[] data, string? name = null, byte extraFlags = 0, uint mtime = 0, byte[]? extra = null, string? comment = null)
    {
        using var output = new MemoryStream();
        byte flags = (byte)((name is null ? 0 : 0x08) | (extra is null ? 0 : 0x04) | (comment is null ? 0 : 0x10) | extraFlags);
        output.Write([0x1F, 0x8B, 8, flags]);
        Span<byte> time = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(time, mtime);
        output.Write(time);
        output.Write([0, 0xFF]);
        if (extra is not null)
        {
            output.Write([(byte)extra.Length, (byte)(extra.Length >> 8)]);
            output.Write(extra);
        }
        if (name is not null)
            output.Write([.. Encoding.ASCII.GetBytes(name), 0]);
        if (comment is not null)
            output.Write([.. Encoding.ASCII.GetBytes(comment), 0]);
        if ((extraFlags & 0x02) != 0)
        {
            // FHCRC: the low 16 bits of the CRC-32 of the header so far (RFC 1952, 2.3.1).
            ushort headerCrc = (ushort)Crc32.Compute(new MemoryStream(output.ToArray()));
            output.Write([(byte)headerCrc, (byte)(headerCrc >> 8)]);
        }
        // DeflateStream writes nothing at all for no input (.claude/rules/core.md); gzip(1) writes
        // the empty final block.
        if (data.Length == 0)
            output.Write([0x03, 0x00]);
        else
        {
            using var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true);
            deflate.Write(data);
        }
        Span<byte> trailer = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(trailer, Crc32.Compute(new MemoryStream(data)));
        BinaryPrimitives.WriteUInt32LittleEndian(trailer[4..], (uint)data.Length);
        output.Write(trailer);
        return output.ToArray();
    }

    private string Write(string name, byte[] bytes)
    {
        string path = Path.Combine(_temp.Path, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static (string Text, BareGzip.ReadResult Result) ReadText(string path)
    {
        using var output = new MemoryStream();
        BareGzip.ReadResult result = BareGzip.Inflate(path, output, CancellationToken.None);
        return (Encoding.ASCII.GetString(output.ToArray()), result);
    }

    private static byte[] Tar()
    {
        using var stream = new MemoryStream();
        using (var writer = new TarWriter(stream, TarEntryFormat.Ustar, leaveOpen: true))
            writer.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, "a.txt") { DataStream = new MemoryStream("hello"u8.ToArray()) });
        return stream.ToArray();
    }

    private static readonly byte[] Text = "plain text, no archive\n"u8.ToArray();

    // --- Happy path: what is a bare gzip ---

    [Fact]
    public void IsBareGzip_TextInside_IsTrue() =>
        BareGzip.IsBareGzip(Write("t.txt.gz", Member(Text))).Should().BeTrue();

    [Fact]
    public void IsBareGzip_EmptyContent_IsTrue() =>
        BareGzip.IsBareGzip(Write("e.gz", Member([]))).Should().BeTrue();

    [Fact]
    public void IsBareGzip_ATarInside_IsFalse() =>
        BareGzip.IsBareGzip(Write("x.tar.gz", Member(Tar()))).Should().BeFalse();

    [Fact]
    public void IsBareGzip_AnEmptyTarInside_IsFalse() =>
        BareGzip.IsBareGzip(Write("empty.tar.gz", Member(new byte[1024]))).Should().BeFalse();

    // tar.exe lists cpio.gz and a zip inside gzip today (measured): they stay on the tar route.
    [Theory]
    [InlineData("070701")]
    [InlineData("070707")]
    [InlineData("PK\u0003\u0004")]
    [InlineData("!<arch>\n")]
    [InlineData("#mtree")]
    [InlineData("BZh9")]
    public void IsBareGzip_AnotherFormatInside_IsFalse(string signature) =>
        BareGzip.IsBareGzip(Write("x.gz", Member([.. Encoding.ASCII.GetBytes(signature), .. Text]))).Should().BeFalse();

    [Fact]
    public void IsBareGzip_AGzipInside_IsFalse() =>
        BareGzip.IsBareGzip(Write("x.gz.gz", Member(Member(Text)))).Should().BeFalse();

    // T-F457 item 1: an ISO 9660 or UDF image starts with a zero system area, which reads as the
    // zero block of an empty tar; through tar.exe it would miss the UDF refusal and the cut check
    // (measured). It is one file, the image, as 7-Zip sees it.
    private const int Sector = 2048;

    private static byte[] DiscImage(int length, params string[] identifiers)
    {
        byte[] bytes = new byte[length];
        for (int i = 0; i < identifiers.Length; i++)
        {
            int at = (16 + i) * Sector;
            bytes[at] = identifiers[i] == "CD001" ? (byte)1 : (byte)0;
            Encoding.ASCII.GetBytes(identifiers[i]).CopyTo(bytes, at + 1);
            bytes[at + 6] = 1;
        }
        return bytes;
    }

    [Fact]
    public void IsBareGzip_AnIso9660ImageInside_IsTrue() =>
        BareGzip.IsBareGzip(Write("a.iso.gz", Member(DiscImage(80 * Sector, "CD001")))).Should().BeTrue();

    [Fact]
    public void IsBareGzip_AUdfImageInside_IsTrue() =>
        BareGzip.IsBareGzip(Write("win.iso.gz", Member(DiscImage(80 * Sector, "CD001", "BEA01", "NSR02", "TEA01")))).Should().BeTrue();

    [Fact]
    public void IsBareGzip_AnImageWhoseLastDescriptorIsTheSixteenth_IsTrue() =>
        BareGzip.IsBareGzip(Write("a.iso.gz", Member(DiscImage(32 * Sector, [.. Enumerable.Repeat("BEA01", 15), "NSR03"])))).Should().BeTrue();

    [Fact]
    public void IsBareGzip_ZerosWithoutADescriptor_StaysOnTheTarRoute() =>
        BareGzip.IsBareGzip(Write("z.gz", Member(new byte[80 * Sector]))).Should().BeFalse();

    // A stream that ends before sector 16 holds no image to name; tar.exe gets it as before.
    [Fact]
    public void IsBareGzip_ZerosEndingBeforeTheDescriptors_StaysOnTheTarRoute() =>
        BareGzip.IsBareGzip(Write("z.gz", Member(new byte[16 * Sector + 3]))).Should().BeFalse();

    // Only the zero system area is looked past: a tar whose data holds an image's identifier at the
    // image's offset stays a tar.
    [Fact]
    public void IsBareGzip_ATarHoldingADescriptorAtTheImageOffset_IsFalse()
    {
        byte[] tar = [.. Tar(), .. new byte[40 * Sector]];
        "\u0001CD001\u0001"u8.CopyTo(tar.AsSpan(16 * Sector));
        BareGzip.IsBareGzip(Write("x.tar.gz", Member(tar))).Should().BeFalse();
    }

    // The image may come after an empty member, as bsdtar reads past one.
    [Fact]
    public void IsBareGzip_AnImageAfterAnEmptyMember_IsTrue() =>
        BareGzip.IsBareGzip(Write("a.iso.gz", [.. Member([]), .. Member(DiscImage(40 * Sector, "CD001"))])).Should().BeTrue();

    // --- Security & Boundary ---

    [Fact]
    public void IsBareGzip_NotGzip_IsFalse() =>
        BareGzip.IsBareGzip(Write("x.gz", Text)).Should().BeFalse();

    [Fact]
    public void IsBareGzip_EmptyFile_IsFalse() =>
        BareGzip.IsBareGzip(Write("x.gz", [])).Should().BeFalse();

    [Fact]
    public void IsBareGzip_MissingFile_IsFalse() =>
        BareGzip.IsBareGzip(Path.Combine(_temp.Path, "none.gz")).Should().BeFalse();

    // A tar header with a checksum that does not hold is no tar: text that happens to be 512 bytes.
    [Fact]
    public void IsBareGzip_TarHeaderWithAWrongChecksum_IsTrue()
    {
        byte[] tar = Tar();
        tar[0] ^= 0x01;

        BareGzip.IsBareGzip(Write("x.gz", Member(tar))).Should().BeTrue();
    }

    // Old tars summed the header as signed chars; libarchive takes either sum. They differ only
    // when a byte is 0x80 or more - a non-ASCII name.
    [Fact]
    public void IsBareGzip_TarWithASignedChecksum_IsFalse()
    {
        byte[] tar = Tar();
        tar[0] = 0xE9;
        long sum = 8 * ' ';
        for (int i = 0; i < 512; i++)
        {
            if (i < 148 || i >= 156)
                sum += (sbyte)tar[i];
        }
        System.Text.Encoding.ASCII.GetBytes(Convert.ToString(sum, 8).PadLeft(6, '0') + "\0 ").CopyTo(tar, 148);

        BareGzip.IsBareGzip(Write("x.tar.gz", Member(tar))).Should().BeFalse();
    }

    // --- The name inside: 7-Zip's GetDefaultName2 with the gzip handler's extensions ---

    [Theory]
    [InlineData("notes.txt.gz", "notes.txt")]
    [InlineData("NOTES.TXT.GZ", "NOTES.TXT")]
    [InlineData("x.gzip", "x")]
    [InlineData("x.tgz", "x.tar")]
    [InlineData("x.tpz", "x.tar")]
    [InlineData("x.bin", "x")]
    [InlineData("noext", "noext~")]
    [InlineData(".gz", ".gz~")]
    [InlineData("a .gz", "a")]
    public void OutputName_IsSevenZipsDefaultName(string archive, string expected) =>
        BareGzip.OutputName(Path.Combine(_temp.Path, archive)).Should().Be(expected);

    // --- Reading ---

    [Fact]
    public void Read_OneMember_IsItsBytes()
    {
        (string text, BareGzip.ReadResult result) = ReadText(Write("t.gz", Member(Text, mtime: 1_700_000_000)));

        text.Should().Be(Encoding.ASCII.GetString(Text));
        result.Should().Be(new BareGzip.ReadResult(Text.Length, false, DateTime.UnixEpoch.AddSeconds(1_700_000_000)));
    }

    [Fact]
    public void Read_EveryHeaderField_IsSkipped()
    {
        byte[] member = Member(Text, name: "inner.txt", extraFlags: 0x02, extra: [1, 2, 3], comment: "a comment");

        (string text, BareGzip.ReadResult result) = ReadText(Write("t.gz", member));

        text.Should().Be(Encoding.ASCII.GetString(Text));
        result.Damaged.Should().BeFalse();
        result.ModifiedUtc.Should().BeNull("an MTIME of 0 means none");
    }

    // Review of T-F413: the header CRC is checked, so a damaged name or comment is not clean.
    [Fact]
    public void Read_WrongHeaderCrc_IsDamaged()
    {
        byte[] member = Member(Text, name: "inner.txt", extraFlags: 0x02);
        member[10] ^= 0x20; // a letter of the stored name, which the header CRC covers

        BareGzip.Inflate(Write("h.gz", member), null, CancellationToken.None).Damaged.Should().BeTrue();
    }

    [Fact]
    public void Read_TwoMembers_AreOneFile()
    {
        (string text, BareGzip.ReadResult result) = ReadText(Write("m.gz", [.. Member("one\n"u8.ToArray()), .. Member("two\n"u8.ToArray())]));

        text.Should().Be("one\ntwo\n");
        result.Should().Be(new BareGzip.ReadResult(8, false, null), "the whole size, where 7-Zip lists the last member's 4");
    }

    [Fact]
    public void Read_EmptyContent_IsNoBytes() =>
        ReadText(Write("e.gz", Member([]))).Result.Should().Be(new BareGzip.ReadResult(0, false, null));

    // Past DeflateStream's input buffer, so the trailer is not in the first read.
    [Fact]
    public void Read_LargeIncompressibleFile_IsWhole()
    {
        byte[] data = new byte[3 * 1024 * 1024 + 17];
        new Random(7).NextBytes(data);
        string path = Write("big.gz", [.. Member(data), .. Member(data)]);

        using var output = new MemoryStream();
        BareGzip.ReadResult result = BareGzip.Inflate(path, output, CancellationToken.None);

        result.Damaged.Should().BeFalse();
        output.ToArray().Should().Equal([.. data, .. data]);
    }

    // --- Error path: what GZipStream lets through ---

    [Fact]
    public void Read_WrongCrc_IsDamaged()
    {
        byte[] member = Member(Text);
        member[^8] ^= 0xFF;

        ReadText(Write("crc.gz", member)).Result.Damaged.Should().BeTrue();
    }

    [Fact]
    public void Read_WrongSize_IsDamaged()
    {
        byte[] member = Member(Text);
        member[^1] ^= 0x01;

        ReadText(Write("size.gz", member)).Result.Damaged.Should().BeTrue();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(12)]
    public void Read_CutShort_IsDamaged(int cut)
    {
        byte[] member = Member(Text);

        ReadText(Write("cut.gz", member[..^cut])).Result.Damaged.Should().BeTrue();
    }

    [Fact]
    public void Read_BytesAfterTheLastMember_IsDamaged() =>
        ReadText(Write("tail.gz", [.. Member(Text), .. "garbage!"u8.ToArray()])).Result.Damaged.Should().BeTrue();

    [Fact]
    public void Read_SecondMemberCutShort_IsDamaged()
    {
        byte[] second = Member("two\n"u8.ToArray());

        ReadText(Write("m.gz", [.. Member(Text), .. second[..^3]])).Result.Damaged.Should().BeTrue();
    }

    [Fact]
    public void Read_ReservedFlag_IsDamaged() =>
        ReadText(Write("r.gz", Member(Text, extraFlags: 0x20))).Result.Damaged.Should().BeTrue();

    [Fact]
    public void Read_NameNeverEnds_IsDamaged()
    {
        byte[] header = [0x1F, 0x8B, 8, 0x08, 0, 0, 0, 0, 0, 0xFF, (byte)'n', (byte)'a'];

        ReadText(Write("n.gz", header)).Result.Damaged.Should().BeTrue();
    }

    [Fact]
    public void Read_CompressedDataDamaged_IsDamaged()
    {
        byte[] member = Member(Text);
        member[10] = 0xFF; // an invalid deflate block type

        ReadText(Write("d.gz", member)).Result.Damaged.Should().BeTrue();
    }

    [Fact]
    public void Read_Cancelled_Throws()
    {
        byte[] data = new byte[1024 * 1024];
        string path = Write("c.gz", Member(data));

        Action read = () => BareGzip.Inflate(path, null, new CancellationToken(canceled: true));

        read.Should().Throw<OperationCanceledException>();
    }
}
