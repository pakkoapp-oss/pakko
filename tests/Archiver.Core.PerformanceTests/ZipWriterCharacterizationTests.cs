using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.PerformanceTests;

/// <summary>
/// T-F270: pins the on-disk shape of archives Pakko writes, through both writer paths — the
/// sequential <c>ZipArchive</c> path (at most 64 files) and the parallel hand-rolled path (more than
/// 64) — before the .NET 8 -> 10 move. .NET 9 swapped zlib for zlib-ng and changed which
/// general-purpose bits <c>ZipArchive</c> sets, so these are written on .NET 8 first: a behavior
/// change then shows up as an already-committed test going red, not as a test fitted to the new
/// result. The central directory is parsed here by hand, not through Archiver.Core, so the check
/// stays independent of the code under test; <c>7za.exe</c> is the independent reader.
/// </summary>
public sealed class ZipWriterCharacterizationTests : IDisposable
{
    private const ushort EncryptedFlag = 0x0001;
    private const ushort Utf8NameFlag = 0x0800;
    private const ushort StoredMethod = 0;
    private const string CyrillicName = "звіт.txt";

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Theory]
    [InlineData(5)]   // sequential ZipArchive path
    [InlineData(70)]  // parallel hand-rolled path
    public async Task ArchiveAsync_EmptyFileNonAsciiNameAndEmptyFolder_PassesSevenZipIntegrityCheck(int fileCount)
    {
        string archivePath = await ArchiveSampleFolderAsync(fileCount);

        var act = () => SevenZipRunner.Test(archivePath);
        act.Should().NotThrow("both writer paths must produce archives a strict third-party reader accepts");
    }

    [Theory]
    [InlineData(5)]
    [InlineData(70)]
    public async Task ArchiveAsync_EmptyFile_IsStoredNotDeflated(int fileCount)
    {
        string archivePath = await ArchiveSampleFolderAsync(fileCount);

        var empty = ReadCentralDirectory(archivePath).Single(e => e.Name.EndsWith("empty.bin", StringComparison.Ordinal));
        empty.Method.Should().Be(StoredMethod, "a zero-byte deflate stream is not valid deflate for 7-Zip (T-F35 follow-up)");
        empty.UncompressedSize.Should().Be(0);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(70)]
    public async Task ArchiveAsync_NonAsciiName_SetsUtf8FlagAndNoEntryIsMarkedEncrypted(int fileCount)
    {
        string archivePath = await ArchiveSampleFolderAsync(fileCount);

        var entries = ReadCentralDirectory(archivePath);
        (entries.Single(e => e.Name.EndsWith(CyrillicName, StringComparison.Ordinal)).Flags & Utf8NameFlag)
            .Should().Be(Utf8NameFlag, "a non-ASCII name must be marked UTF-8 (bit 11)");
        entries.Should().OnlyContain(e => (e.Flags & EncryptedFlag) == 0, "nothing was encrypted");
    }

    private async Task<string> ArchiveSampleFolderAsync(int fileCount)
    {
        string sourceDir = Path.Combine(_temp.Path, "source");
        Directory.CreateDirectory(Path.Combine(sourceDir, "empty-folder"));
        for (int i = 0; i < fileCount - 2; i++)
            File.WriteAllText(Path.Combine(sourceDir, $"file{i}.txt"), string.Concat(Enumerable.Repeat($"line {i}\n", 200)));
        File.WriteAllBytes(Path.Combine(sourceDir, "empty.bin"), []);
        File.WriteAllText(Path.Combine(sourceDir, CyrillicName), "вміст");

        string destinationDir = Path.Combine(_temp.Path, "dest");
        Directory.CreateDirectory(destinationDir);
        var result = await new ZipArchiveService().ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [sourceDir],
            DestinationFolder = destinationDir,
            ArchiveName = "sample",
            CompressionLevel = CompressionLevel.Optimal,
        }, progress: null, CancellationToken.None);

        result.Errors.Should().BeEmpty();
        return Path.Combine(destinationDir, "sample.zip");
    }

    private sealed record CentralEntry(string Name, ushort Flags, ushort Method, uint UncompressedSize);

    private static List<CentralEntry> ReadCentralDirectory(string archivePath)
    {
        byte[] bytes = File.ReadAllBytes(archivePath);
        int eocd = bytes.Length - 22;
        while (eocd >= 0 && BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(eocd)) != 0x06054b50)
            eocd--;
        eocd.Should().BeGreaterThanOrEqualTo(0, "the archive must end with an end-of-central-directory record");

        int count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(eocd + 10));
        int offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(eocd + 16));
        var entries = new List<CentralEntry>(count);
        for (int i = 0; i < count; i++)
        {
            BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset)).Should().Be(0x02014b50u);
            ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 8));
            ushort method = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 10));
            uint uncompressed = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 24));
            int nameLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 28));
            int extraLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 30));
            int commentLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 32));
            string name = Encoding.UTF8.GetString(bytes, offset + 46, nameLength);
            entries.Add(new CentralEntry(name, flags, method, uncompressed));
            offset += 46 + nameLength + extraLength + commentLength;
        }
        return entries;
    }
}
