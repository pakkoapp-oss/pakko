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
            output.Write([0, 0]);
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
        BareGzip.ReadResult result = BareGzip.Read(path, output, CancellationToken.None);
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
        BareGzip.ReadResult result = BareGzip.Read(path, output, CancellationToken.None);

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

        Action read = () => BareGzip.Read(path, null, new CancellationToken(canceled: true));

        read.Should().Throw<OperationCanceledException>();
    }
}
