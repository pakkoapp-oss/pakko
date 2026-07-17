using System.IO.Compression;
using Archiver.Core.Services.Zip;
using FluentAssertions;

namespace Archiver.Core.Tests.Services.Zip;

public sealed class DosDateTimeTests
{
    [Theory]
    [InlineData(1980, 1, 1, 0, 0, 0)]
    [InlineData(1980, 1, 1, 0, 0, 2)]
    [InlineData(2024, 6, 15, 13, 45, 30)]
    [InlineData(2026, 7, 17, 23, 59, 58)]
    [InlineData(2000, 2, 29, 12, 0, 0)] // leap day
    [InlineData(2107, 12, 31, 23, 59, 58)]
    public void EncodeDecode_RoundTrips_WithinTwoSecondGranularity(int year, int month, int day, int hour, int minute, int second)
    {
        var original = new DateTime(year, month, day, hour, minute, second);

        uint packed = DosDateTime.Encode(original);
        DateTime decoded = DosDateTime.Decode(packed);

        decoded.Should().Be(original);
    }

    [Theory]
    [InlineData(2024, 6, 15, 13, 45, 31)] // odd second truncates to even (2-second granularity)
    [InlineData(2024, 6, 15, 13, 45, 59)]
    public void Encode_OddSeconds_TruncatesToEvenSecond(int year, int month, int day, int hour, int minute, int second)
    {
        var original = new DateTime(year, month, day, hour, minute, second);
        var expectedTruncated = new DateTime(year, month, day, hour, minute, second - 1);

        uint packed = DosDateTime.Encode(original);
        DateTime decoded = DosDateTime.Decode(packed);

        decoded.Should().Be(expectedTruncated);
    }

    [Fact]
    public void Encode_DateBefore1980_ClampsToDosEpoch()
    {
        uint packed = DosDateTime.Encode(new DateTime(1970, 1, 1));

        DosDateTime.Decode(packed).Should().Be(new DateTime(1980, 1, 1, 0, 0, 0));
    }

    [Fact]
    public void Encode_DateAfter2107_ClampsToDosMaximum()
    {
        uint packed = DosDateTime.Encode(new DateTime(2200, 1, 1));

        DosDateTime.Decode(packed).Should().Be(new DateTime(2107, 12, 31, 23, 59, 58));
    }

    [Theory]
    [InlineData(1980, 1, 1, 0, 0, 0)]
    [InlineData(2024, 6, 15, 13, 45, 30)]
    [InlineData(2026, 7, 17, 23, 59, 58)]
    [InlineData(2000, 2, 29, 12, 0, 0)]
    public void Encode_MatchesZipArchiveEntrysOwnEncoding_ByteForByte(int year, int month, int day, int hour, int minute, int second)
    {
        var dateTime = new DateTime(year, month, day, hour, minute, second);

        (ushort referenceTime, ushort referenceDate) = WriteEntryAndReadRawDosFields(dateTime);
        uint packed = DosDateTime.Encode(dateTime);
        ushort actualTime = (ushort)(packed & 0xFFFF);
        ushort actualDate = (ushort)(packed >> 16);

        actualTime.Should().Be(referenceTime);
        actualDate.Should().Be(referenceDate);
    }

    // Builds a single-entry ZIP via System.IO.Compression's own ZipArchiveEntry (the reference
    // implementation) and reads the raw mod-time/mod-date fields straight out of the local file
    // header bytes, so DosDateTime's hand-rolled packing can be proven byte-identical to what
    // ZipArchiveEntry itself produces for the same input DateTime.
    private static (ushort Time, ushort Date) WriteEntryAndReadRawDosFields(DateTime lastWriteTime)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("a.txt");
            entry.LastWriteTime = lastWriteTime;
            using var entryStream = entry.Open();
            entryStream.WriteByte(1);
        }

        byte[] bytes = ms.ToArray();
        // Local file header: signature(4) version(2) flags(2) method(2) modTime(2) modDate(2) ...
        ushort modTime = BitConverter.ToUInt16(bytes, 10);
        ushort modDate = BitConverter.ToUInt16(bytes, 12);
        return (modTime, modDate);
    }
}
