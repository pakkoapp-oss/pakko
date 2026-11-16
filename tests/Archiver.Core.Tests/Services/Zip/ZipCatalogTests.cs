using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Services.Zip;
using Archiver.Core.Services.Zip.Decryption;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services.Zip;

// T-F529: ZipCatalog walks the central directory as a stream and gives .NET's reader one batch of
// records at a time, over an overlay of the file. What it reads must be what .NET's reader of the
// whole file read; what it holds must be one batch.
public sealed class ZipCatalogTests : IDisposable
{
    private static readonly ZipNameCodePages Us = ZipNameCodePages.FromCodePages(437, 1252);
    private readonly string _folder = Directory.CreateTempSubdirectory("pakko-catalog-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    // --- Happy path ---

    // T-F543: the first walks never looked at the token; at millions of entries each takes seconds.
    [Fact]
    public void Check_ACancel_StopsTheWalk()
    {
        string zip = Write("cancel.zip", MixedArchive());
        using var catalog = ZipCatalog.Open(zip, Us);

        FluentActions.Invoking(() => catalog.Check(cancellationToken: new CancellationToken(true)))
            .Should().Throw<OperationCanceledException>();
    }

    [Fact]
    public void MeasureArchive_ACancel_StopsTheWalk()
    {
        string zip = Write("measure.zip", MixedArchive());
        using var catalog = ZipCatalog.Open(zip, Us);

        FluentActions.Invoking(() => ZipArchiveService.MeasureArchive(
                catalog, ZipLocate.None, _ => true, new System.Collections.BitArray(64), null, new CancellationToken(true)))
            .Should().Throw<OperationCanceledException>();
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(4096, false)]
    [InlineData(4096, true)]
    public void Entries_AreWhatDotNetReadsOfTheWholeFile(int batchEntries, bool zip64Overlay)
    {
        string zip = Write("mixed.zip", MixedArchive());

        using var catalog = ZipCatalog.Open(zip, Us, ArchiveLimits.MaxEntries, batchEntries, zip64Overlay);
        var walked = new List<(string Name, long Length, long Compressed, uint Crc, DateTimeOffset Time, string Content)>();
        foreach (NamedZipEntry named in catalog.Entries())
            walked.Add((named.FullName, named.Entry.Length, named.Entry.CompressedLength, named.Entry.Crc32, named.Entry.LastWriteTime, Content(named.Entry)));

        using ZipArchive whole = ZipFile.OpenRead(zip);
        walked.Should().Equal(whole.Entries.Select(e => (e.FullName, e.Length, e.CompressedLength, e.Crc32, e.LastWriteTime, Content(e))));
    }

    [Fact]
    public void Entries_CarryTheirIndexInTheDirectory()
    {
        string zip = Write("mixed.zip", MixedArchive());

        using var catalog = ZipCatalog.Open(zip, Us, ArchiveLimits.MaxEntries, batchEntries: 2, forceZip64Overlay: false);

        catalog.Entries().Select(e => e.Index).Should().Equal(0, 1, 2, 3, 4);
        catalog.Names().Select(n => n.Index).Should().Equal(0, 1, 2, 3, 4);
    }

    [Fact]
    public void Deflate64_IsReadAsDotNetReadsIt()
    {
        // 7-Zip's -mm=Deflate64 output for "deflate64.txt" (300 copies of "Pakko "): .NET decodes
        // method 9 itself, which T-F529 keeps by reading entries through .NET's reader.
        string zip = Write("d64.zip", Deflate64Archive());

        using var catalog = ZipCatalog.Open(zip, Us);

        catalog.Entries().Select(e => (e.FullName, Content(e.Entry)))
            .Should().Equal(("deflate64.txt", string.Concat(Enumerable.Repeat("Pakko ", 300))));
    }

    // --- Security & Boundary ---

    [Fact]
    public void Entries_OfAnEarlierBatch_AreGoneOnceTheWalkMovesOn()
    {
        string zip = Write("mixed.zip", MixedArchive());
        using var catalog = ZipCatalog.Open(zip, Us, ArchiveLimits.MaxEntries, batchEntries: 2, forceZip64Overlay: false);

        NamedZipEntry? first = null;
        foreach (NamedZipEntry named in catalog.Entries())
        {
            if (named.Index == 0)
                first = named;
            if (named.Index == 2)
                break;
        }

        // The first batch's archive was disposed when the third entry's batch was read.
        FluentActions.Invoking(() => first!.Entry.Open()).Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Collision_AcrossABatchBoundary_IsFound()
    {
        // "é.txt" with the UTF-8 flag, then the CP437 byte 0x82 ("é" too) without it.
        string zip = Write("collide.zip", CollidingArchive());

        using var catalog = ZipCatalog.Open(zip, Us, ArchiveLimits.MaxEntries, batchEntries: 1, forceZip64Overlay: false);

        catalog.Entries(collisions: true).Select(e => (e.FullName, e.CollidesAfterDecoding))
            .Should().Equal(("é.txt", false), ("é.txt", true));
    }

    // T-F529 review: a long raw name is compared by its digest, not held; what collides still does.
    [Theory]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(60_000)]
    public void Collision_OfLongRawNames_IsFound_AndTheSameRawNameIsNot(int rawLength)
    {
        byte[] Raw(char c) => Encoding.ASCII.GetBytes(new string(c, rawLength));
        LegacyZipBuilder.Entry Named(byte[] raw) =>
            new(raw, [], CentralExtra: LegacyZipBuilder.UnicodePathExtra(raw, "same.txt"u8.ToArray()));
        string zip = LegacyZipBuilder.Write(Path.Combine(_folder, $"long{rawLength}.zip"),
            Named(Raw('a')), Named(Raw('a')), Named(Raw('b')));

        using var catalog = ZipCatalog.Open(zip, Us, ArchiveLimits.MaxEntries, batchEntries: 1, forceZip64Overlay: false);

        catalog.Entries(collisions: true).Select(e => e.CollidesAfterDecoding).Should().Equal(false, false, true);
    }

    [Fact]
    public void ADataRegionRunningPastTheFile_IsRefusedAsDotNetRefusesIt()
    {
        // A stored entry whose compressed size reaches into where the overlay's records lie: once
        // the tail is hidden, .NET sees the file's own length, as it did reading the file.
        byte[] bytes = MixedArchive();
        int stored = IndexOf(bytes, "stored.txt"u8, central: true);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(stored - 46 + 20), (uint)bytes.Length);
        string zip = Write("past.zip", bytes);

        using var catalog = ZipCatalog.Open(zip, Us);

        foreach (NamedZipEntry entry in catalog.Entries().Where(e => e.FullName == "stored.txt"))
            FluentActions.Invoking(() => entry.Entry.Open()).Should().Throw<InvalidDataException>();
    }

    // T-F538: an unmasked classic end record points at one directory, a Zip64 locator at another of
    // the same count. .NET reads the classic one; before T-F529 the names came from the other.
    [Fact]
    public void TwoDirectories_NamesAndDataComeFromTheOneDotNetReads()
    {
        string zip = Write("two.zip", TwoDirectoriesArchive());

        using var catalog = ZipCatalog.Open(zip, Us);

        catalog.Entries().Select(e => (e.FullName, Content(e.Entry))).Should().Equal(("a.txt", "A"), ("b.txt", "B"));
    }

    [Fact]
    public async Task TwoDirectories_AreListedByTheOneDotNetReads()
    {
        string zip = Write("two.zip", TwoDirectoriesArchive());

        ArchiveListResult listed = await new ZipArchiveService(new GroupPolicyOptions()).ListEntriesAsync(zip);

        listed.Entries.Select(e => e.Path).Should().Equal("a.txt", "b.txt");
    }

    // --- Misuse & Error path ---

    [Fact]
    public void MoreRecordsThanDeclared_IsRefusedWithDotNetsText()
    {
        byte[] bytes = MixedArchive();
        int eocd = bytes.Length - 22;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(eocd + 8), 4);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(eocd + 10), 4);
        string zip = Write("more.zip", bytes);

        using var catalog = ZipCatalog.Open(zip, Us);

        FluentActions.Invoking(() => catalog.Entries().ToList()).Should().Throw<InvalidDataException>()
            .WithMessage(RawZipEntryLocator.NumEntriesWrongText);
        FluentActions.Invoking(() => catalog.Check()).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void ASplitArchive_IsRefusedWithDotNetsText()
    {
        byte[] bytes = MixedArchive();
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(bytes.Length - 22 + 6), 1);
        string zip = Write("split.zip", bytes);
        using var catalog = ZipCatalog.Open(zip, Us);

        FluentActions.Invoking(() => catalog.Check()).Should().Throw<InvalidDataException>()
            .WithMessage("Split or spanned archives are not supported.");
    }

    [Fact]
    public void ACommentRunningPastTheEnd_IsRefused()
    {
        byte[] bytes = MixedArchive();
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(bytes.Length - 2), 100);
        string zip = Write("comment.zip", bytes);
        using var catalog = ZipCatalog.Open(zip, Us);

        // As .NET's reader, which takes such a record for none.
        FluentActions.Invoking(() => catalog.Check()).Should().Throw<InvalidDataException>()
            .WithMessage("End of Central Directory record could not be found.");
    }

    // --- Helpers ---

    private string Write(string name, byte[] bytes)
    {
        string path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static string Content(ZipArchiveEntry entry)
    {
        if (entry.FullName.EndsWith('/'))
            return "";
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static byte[] MixedArchive()
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "folder/", null, CompressionLevel.Optimal);
            Add(zip, "folder/deflated.txt", string.Concat(Enumerable.Repeat("deflate me ", 50)), CompressionLevel.Optimal);
            Add(zip, "stored.txt", "stored", CompressionLevel.NoCompression);
            Add(zip, "Привіт.txt", "utf-8 name", CompressionLevel.Optimal);
            Add(zip, "empty.txt", "", CompressionLevel.Optimal);
        }
        return buffer.ToArray();
    }

    private static void Add(ZipArchive zip, string name, string? content, CompressionLevel level)
    {
        ZipArchiveEntry entry = zip.CreateEntry(name, level);
        entry.LastWriteTime = new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
        if (content is null)
            return;
        using Stream stream = entry.Open();
        stream.Write(Encoding.UTF8.GetBytes(content));
    }

    private static byte[] CollidingArchive()
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "é.txt", "first", CompressionLevel.NoCompression);
            Add(zip, "Q.txt", "second", CompressionLevel.NoCompression);
        }
        byte[] bytes = buffer.ToArray();
        // 'Q' becomes 0x82 in the local header and the directory: CP437's "é", no UTF-8 flag.
        bytes[IndexOf(bytes, "Q.txt"u8, central: false)] = 0x82;
        bytes[IndexOf(bytes, "Q.txt"u8, central: true)] = 0x82;
        return bytes;
    }

    // [local a][local b][directory 1: a, b][gap][directory 2: z, y][Zip64 end -> 2][locator][end -> 1]
    private static byte[] TwoDirectoriesArchive()
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "a.txt", "A", CompressionLevel.NoCompression);
            Add(zip, "b.txt", "B", CompressionLevel.NoCompression);
        }
        byte[] plain = buffer.ToArray();
        int eocd = plain.Length - 22;
        int directorySize = (int)BinaryPrimitives.ReadUInt32LittleEndian(plain.AsSpan(eocd + 12));
        int directoryOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(plain.AsSpan(eocd + 16));

        byte[] second = plain.AsSpan(directoryOffset, directorySize).ToArray();
        second[IndexOf(second, "a.txt"u8, central: false)] = (byte)'z';
        second[IndexOf(second, "b.txt"u8, central: false)] = (byte)'y';

        var result = new MemoryStream();
        result.Write(plain.AsSpan(0, directoryOffset + directorySize));
        // A gap: .NET's reader reads records until a signature that is not one, so the second
        // directory right after the first would be counted as more of it.
        result.Write(new byte[4]);
        long secondOffset = result.Position;
        result.Write(second);
        long zip64End = result.Position;
        Span<byte> record = stackalloc byte[56];
        BinaryPrimitives.WriteUInt32LittleEndian(record, 0x06064b50);
        BinaryPrimitives.WriteUInt64LittleEndian(record[4..], 44);
        BinaryPrimitives.WriteUInt16LittleEndian(record[12..], 45);
        BinaryPrimitives.WriteUInt16LittleEndian(record[14..], 45);
        BinaryPrimitives.WriteInt64LittleEndian(record[24..], 2);
        BinaryPrimitives.WriteInt64LittleEndian(record[32..], 2);
        BinaryPrimitives.WriteInt64LittleEndian(record[40..], second.Length);
        BinaryPrimitives.WriteInt64LittleEndian(record[48..], secondOffset);
        result.Write(record);
        Span<byte> locator = stackalloc byte[20];
        BinaryPrimitives.WriteUInt32LittleEndian(locator, 0x07064b50);
        BinaryPrimitives.WriteInt64LittleEndian(locator[8..], zip64End);
        BinaryPrimitives.WriteUInt32LittleEndian(locator[16..], 1);
        result.Write(locator);
        result.Write(plain.AsSpan(eocd));
        return result.ToArray();
    }

    // The name as it stands in the directory (central) or the first local header that holds it.
    private static int IndexOf(byte[] bytes, ReadOnlySpan<byte> name, bool central)
    {
        int index = central ? bytes.AsSpan().LastIndexOf(name) : bytes.AsSpan().IndexOf(name);
        index.Should().BeGreaterThanOrEqualTo(0);
        return index;
    }

    private static byte[] Deflate64Archive() => Convert.FromBase64String(Deflate64Base64);

    private const string Deflate64Base64 = "UEsDBBUAAAAJAOQLcF0KQQVwHgAAAAgHAAANAAAAZGVmbGF0ZTY0LnR4dOXEIQEAAAgDsCpEogL6gv7uQTaxveSHn5+fn59fvlBLAQI/ABUAAAAJAOQLcF0KQQVwHgAAAAgHAAANACQAAAAAAAAAIAAAAAAAAABkZWZsYXRlNjQudHh0CgAgAAAAAAABABgAJB3SY1l13QEAAAAAAAAAAAAAAAAAAAAAUEsFBgAAAAABAAEAXwAAAEkAAAAAAA==";
}
