using System.Globalization;
using System.IO.Compression;
using System.Text;
using Archiver.Core.IO;
using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.Tests.IO;

public sealed class VerifyingReadStreamTests
{
    private static readonly byte[] Data = Encoding.ASCII.GetBytes("verifying-read-stream content");
    private static readonly uint DataCrc = Crc32.Compute(new MemoryStream(Data));

    [Fact]
    public void Read_ExactDeclaredLengthAndMatchingCrc_ReturnsContent()
    {
        using var s = new VerifyingReadStream(new MemoryStream(Data), Data.Length, DataCrc);
        using var copy = new MemoryStream();
        s.CopyTo(copy);
        copy.ToArray().Should().Equal(Data);
    }

    [Fact]
    public async Task ReadAsync_ExactDeclaredLengthAndMatchingCrc_ReturnsContent()
    {
        await using var s = new VerifyingReadStream(new MemoryStream(Data), Data.Length, DataCrc);
        using var copy = new MemoryStream();
        await s.CopyToAsync(copy);
        copy.ToArray().Should().Equal(Data);
    }

    [Fact]
    public void Read_ContentLongerThanDeclared_Throws()
    {
        using var s = new VerifyingReadStream(new MemoryStream(Data), Data.Length - 1, expectedCrc32: null);
        Action act = () => s.CopyTo(Stream.Null);
        act.Should().Throw<InvalidDataException>().WithMessage("*declared size*");
    }

    [Fact]
    public async Task ReadAsync_WrongCrc_ThrowsAtEndOfStream()
    {
        await using var s = new VerifyingReadStream(new MemoryStream(Data), Data.Length, DataCrc ^ 1);
        Func<Task> act = () => s.CopyToAsync(Stream.Null);
        await act.Should().ThrowAsync<InvalidDataException>().WithMessage("*CRC-32*");
    }

    // T-F297: Core's own text carries its code, so a frontend can add a translation.
    [Fact]
    public void Read_WrongCrc_ErrorCarriesItsCode()
    {
        using var s = new VerifyingReadStream(new MemoryStream(Data), Data.Length, DataCrc ^ 1);
        Action act = () => s.CopyTo(Stream.Null);

        InvalidDataException ex = act.Should().Throw<InvalidDataException>().Which;
        CoreText detail = CoreMessages.Detail(ex);
        detail.Code.Should().Be(MessageCode.ContentCrcMismatch);
        detail.English.Should().Be($"Content failed CRC-32 check (expected {DataCrc ^ 1:X8}, got {DataCrc:X8}).");
    }

    // T-F333: the two size errors carry a code too, so Shell and App do not show bare English.
    [Theory]
    [InlineData(-1, MessageCode.ContentLargerThanDeclared, "Content is larger than its declared size ({0} bytes).")]
    [InlineData(1, MessageCode.ContentSmallerThanDeclared, "Content is smaller than its declared size ({0} bytes).")]
    public void Read_ContentNotOfDeclaredSize_ErrorCarriesItsCode(int declaredDelta, MessageCode expectedCode, string expectedEnglish)
    {
        long declared = Data.Length + declaredDelta;
        using var s = new VerifyingReadStream(new MemoryStream(Data), declared, expectedCrc32: null);
        Action act = () => s.CopyTo(Stream.Null);

        InvalidDataException ex = act.Should().Throw<InvalidDataException>().Which;
        CoreText detail = CoreMessages.Detail(ex);
        detail.Code.Should().Be(expectedCode);
        detail.English.Should().Be(string.Format(CultureInfo.InvariantCulture, expectedEnglish, declared));
        ex.Message.Should().Be(detail.English);
    }

    // T-F382: the decompressor's own failure is English and names the wrong cause.
    [Fact]
    public void Read_DecompressorFails_ErrorCarriesTheDamagedCode()
    {
        using var s = new VerifyingReadStream(new DeflateStream(new MemoryStream(new byte[64]), CompressionMode.Decompress), 1000, expectedCrc32: null);
        Action act = () => s.CopyTo(Stream.Null);

        CoreMessages.Detail(act.Should().Throw<InvalidDataException>().Which).Code.Should().Be(MessageCode.ContentDamaged);
    }

    [Fact]
    public async Task ReadAsync_DecompressorFails_ErrorCarriesTheDamagedCode()
    {
        await using var s = new VerifyingReadStream(new DeflateStream(new MemoryStream(new byte[64]), CompressionMode.Decompress), 1000, expectedCrc32: null);
        Func<Task> act = () => s.CopyToAsync(Stream.Null);

        CoreMessages.Detail((await act.Should().ThrowAsync<InvalidDataException>()).Which).Code.Should().Be(MessageCode.ContentDamaged);
    }

    [Fact]
    public void Read_EmptyContentWithZeroCrc_Succeeds()
    {
        using var s = new VerifyingReadStream(new MemoryStream(), 0, 0u);
        Action act = () => s.CopyTo(Stream.Null);
        act.Should().NotThrow();
    }

    [Fact]
    public void Read_ContentShorterThanDeclared_ThrowsAtEndOfStream()
    {
        using var s = new VerifyingReadStream(new MemoryStream(Data), Data.Length + 100, expectedCrc32: null);
        Action act = () => s.CopyTo(Stream.Null);
        act.Should().Throw<InvalidDataException>().WithMessage("*smaller than its declared size*");
    }

    [Fact]
    public async Task ReadAsync_ContentShorterThanDeclared_ThrowsAtEndOfStream()
    {
        await using var s = new VerifyingReadStream(new MemoryStream(Data), Data.Length + 1, DataCrc);
        Func<Task> act = () => s.CopyToAsync(Stream.Null);
        await act.Should().ThrowAsync<InvalidDataException>().WithMessage("*smaller than its declared size*");
    }

    // T-F492: .NET 11's ZipArchiveEntry.Open() checks CRC-32 and the declared size itself and throws
    // on the read that reaches the declared size, so the last chunk never reaches this stream.
    // VerifyingReadStream recognises those two throws by their text; these pin the text.
    private static ZipArchive OpenOneEntryZip(CompressionLevel level, uint? patchedCrc = null, uint? patchedSize = null)
    {
        var buffer = new MemoryStream();
        using (var create = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using Stream w = create.CreateEntry("e.txt", level).Open();
            w.Write(Data);
        }
        byte[] bytes = buffer.ToArray();
        for (int i = 0; i + 4 <= bytes.Length; i++)
        {
            uint sig = BitConverter.ToUInt32(bytes, i);
            int crcAt = sig == 0x04034b50 ? i + 14 : sig == 0x02014b50 ? i + 16 : -1;
            if (crcAt < 0)
                continue;
            if (patchedCrc is { } crc)
                BitConverter.GetBytes(crc).CopyTo(bytes, crcAt);
            if (patchedSize is { } size)
                BitConverter.GetBytes(size).CopyTo(bytes, crcAt + 8);
        }
        return new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
    }

    [Fact]
    public void BclCrcMismatchText_IsWhatZipArchiveThrows()
    {
        using ZipArchive zip = OpenOneEntryZip(CompressionLevel.NoCompression, patchedCrc: DataCrc ^ 1);
        using Stream s = zip.Entries[0].Open();
        Action act = () => s.CopyTo(Stream.Null);
        act.Should().Throw<InvalidDataException>().Which.Message.Should().Be(VerifyingReadStream.BclCrcMismatchText);
    }

    [Fact]
    public void BclLengthMismatchText_IsWhatZipArchiveThrows()
    {
        using ZipArchive zip = OpenOneEntryZip(CompressionLevel.NoCompression, patchedSize: 10);
        using Stream s = zip.Entries[0].Open();
        Action act = () => s.CopyTo(Stream.Null);
        act.Should().Throw<InvalidDataException>().Which.Message.Should().Be(VerifyingReadStream.BclLengthMismatchText);
    }

    [Theory]
    [InlineData(CompressionLevel.NoCompression)]
    [InlineData(CompressionLevel.Optimal)]
    public void Read_ZipEntryWithWrongCrc_ReportsTheComputedCrc(CompressionLevel level)
    {
        using ZipArchive zip = OpenOneEntryZip(level, patchedCrc: 0xDEADBEEF);
        ZipArchiveEntry entry = zip.Entries[0];
        using var s = new VerifyingReadStream(entry.Open(), entry.Length, entry.Crc32);
        Action act = () => s.CopyTo(Stream.Null);

        InvalidDataException ex = act.Should().Throw<InvalidDataException>().Which;
        CoreText detail = CoreMessages.Detail(ex);
        detail.Code.Should().Be(MessageCode.ContentCrcMismatch);
        ex.Message.Should().Contain("DEADBEEF").And.Contain(DataCrc.ToString("X8", CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task ReadAsync_ZipEntryWithWrongCrc_ReportsTheComputedCrc()
    {
        using ZipArchive zip = OpenOneEntryZip(CompressionLevel.Optimal, patchedCrc: 0xDEADBEEF);
        ZipArchiveEntry entry = zip.Entries[0];
        await using var s = new VerifyingReadStream(entry.Open(), entry.Length, entry.Crc32);
        Func<Task> act = () => s.CopyToAsync(Stream.Null);

        InvalidDataException ex = (await act.Should().ThrowAsync<InvalidDataException>()).Which;
        CoreMessages.Detail(ex).Code.Should().Be(MessageCode.ContentCrcMismatch);
        ex.Message.Should().Contain(DataCrc.ToString("X8", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Read_StoredZipEntryLargerThanDeclared_ReportsTheDeclaredSize()
    {
        using ZipArchive zip = OpenOneEntryZip(CompressionLevel.NoCompression, patchedSize: 10);
        ZipArchiveEntry entry = zip.Entries[0];
        using var s = new VerifyingReadStream(entry.Open(), entry.Length, entry.Crc32);
        Action act = () => s.CopyTo(Stream.Null);

        CoreMessages.Detail(act.Should().Throw<InvalidDataException>().Which).Code.Should().Be(MessageCode.ContentLargerThanDeclared);
    }
}
