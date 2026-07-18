using System.Buffers.Binary;
using System.IO.Compression;
using Archiver.Core.Models;
using Archiver.Core.Services.Zip;
using FluentAssertions;

namespace Archiver.Core.PerformanceTests;

/// <summary>
/// T-F35: proves <c>ZipEntryWriter</c>'s hand-rolled ZIP container bytes are independently
/// readable, not just self-consistent with its own code. Lives in this project (rather than
/// Archiver.Core.Tests) specifically to reuse the vendored, hash-verified, sandboxed <c>7za.exe</c>
/// binary already vendored here for T-F114 — an independent third-party ZIP reader is the
/// strongest available signal that the hand-written format bytes are actually spec-compliant,
/// not merely "agrees with itself." This is a correctness/compatibility suite, not a performance
/// one, despite sharing the project — see TESTING.md.
/// </summary>
public sealed class ZipEntryWriterCompatibilityTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task WrittenArchive_OpensViaSystemIOCompression_ContentMatchesSource()
    {
        (string archivePath, var expected) = await BuildMixedArchiveAsync();

        using var archive = ZipFile.OpenRead(archivePath);

        archive.Entries.Should().HaveCount(expected.Count);
        foreach (var (entryName, expectedBytes) in expected)
        {
            var entry = archive.GetEntry(entryName);
            entry.Should().NotBeNull($"entry '{entryName}' should be present");
            if (expectedBytes is null)
            {
                entry!.Length.Should().Be(0);
                continue;
            }

            using var stream = entry!.Open();
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            ms.ToArray().Should().Equal(expectedBytes);
        }
    }

    [Fact]
    public async Task WrittenArchive_PassesSevenZipIntegrityCheck()
    {
        if (!SevenZipRunner.IsAvailable) return; // defense-in-depth only, see SevenZipRunner

        (string archivePath, _) = await BuildMixedArchiveAsync();

        var act = () => SevenZipRunner.Test(archivePath);
        act.Should().NotThrow("an independent third-party ZIP reader must accept the hand-rolled container bytes");
    }

    [Fact]
    public async Task WrittenArchive_RawStructuralParse_SignaturesAndOffsetsAreValid()
    {
        (string archivePath, var expected) = await BuildMixedArchiveAsync();
        byte[] bytes = await File.ReadAllBytesAsync(archivePath);

        RawZipStructure structure = RawZipStructure.Parse(bytes);

        structure.CentralDirectoryRecords.Should().HaveCount(expected.Count);
        foreach (var record in structure.CentralDirectoryRecords)
        {
            BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan((int)record.LocalHeaderOffset, 4))
                .Should().Be(0x04034b50u, $"central directory record for '{record.Name}' must point at a real local file header");
        }
    }

    [Fact]
    public async Task WriteCompressedEntryFromStreamAsync_LargeDeclaredSizes_EmitsCorrectZip64Fields()
    {
        // Deliberately synthetic: declares compressed/uncompressed sizes far above the Zip64
        // threshold while the actual stream content is a few bytes, to exercise the Zip64 local-
        // header/extra-field write path without needing gigabytes of real data on disk. Both
        // remaining write paths (in-memory and temp-file) know their real sizes fully upfront in
        // production, so this test isn't simulating a real production input shape — it's directly
        // probing WriteCompressedEntryFromStreamAsync's Zip64-field-writing logic in isolation.
        // The resulting file's declared entry size deliberately does not match its real content,
        // so this test verifies only the raw CONTAINER structure (signatures, offsets, the Zip64
        // extra field's own bytes) — not via ZipFile.OpenRead/7za, since both would reasonably
        // reject content that doesn't match its declared length.
        const long hugeSize = 5_000_000_000L;
        string archivePath = Path.Combine(_temp.Path, "zip64-forced.zip");

        await using (var writer = new ZipEntryWriter(archivePath))
        {
            byte[] tinyContent = System.Text.Encoding.UTF8.GetBytes("tiny");
            using var ms = new MemoryStream(tinyContent);
            await writer.WriteCompressedEntryFromStreamAsync(
                "forced.bin", ms, compressedLength: hugeSize, uncompressedLength: hugeSize,
                crc32: 0, ZipEntryWriter.StoredMethod, DateTime.UtcNow, CancellationToken.None);
        }

        byte[] bytes = await File.ReadAllBytesAsync(archivePath);
        RawZipStructure structure = RawZipStructure.Parse(bytes);
        structure.CentralDirectoryRecords.Should().ContainSingle();

        // Parse the local file header's Zip64 extra field directly and confirm it carries the
        // declared huge values — local header: sig(4) verNeeded(2) flags(2) method(2) time(2)
        // date(2) crc(4) compSize(4)=0xFFFFFFFF uncompSize(4)=0xFFFFFFFF nameLen(2) extraLen(2)
        // name(nameLen) extra(tag(2) size(2) uncompressed(8) compressed(8)).
        int nameLen = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(26, 2));
        int extraLen = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(28, 2));
        extraLen.Should().Be(20, "tag(2)+size(2)+two 8-byte values = 20 bytes total for the extra field area");
        int extraStart = 30 + nameLen;
        BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(extraStart, 2)).Should().Be(0x0001, "Zip64 extra field tag");
        BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(extraStart + 2, 2)).Should().Be(16, "the sub-field's own data-size value covers just the two 8-byte values, not the tag+size header");
        ulong declaredUncompressed = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(extraStart + 4, 8));
        ulong declaredCompressed = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(extraStart + 12, 8));
        declaredUncompressed.Should().Be((ulong)hugeSize);
        declaredCompressed.Should().Be((ulong)hugeSize);
    }

    [Fact]
    public async Task ArchiveAsync_RealFolderWithEmptyFilesAndFoldersAboveParallelThreshold_PassesSevenZipIntegrityCheck()
    {
        // End-to-end reproduction of a real on-device bug report: a real folder (containing
        // genuinely empty files like .gitkeep/lockfile alongside normal ones, plus an empty
        // subdirectory) compressed through the actual public ZipArchiveService.ArchiveAsync API —
        // not just ZipEntryCompressor/ZipEntryWriter called directly, like the other tests in this
        // class — so this also exercises the real WorkItemEnumerator/threshold-routing/RunPipelineAsync
        // dispatch path. File count is pushed above ParallelPipelineFileCountThreshold (64) so the
        // parallel pipeline (not the original sequential ZipArchive path) is the one under test.
        // NanaZip (a real 7-Zip engine) rejected exactly this shape with "Data error" on every
        // zero-byte entry before the ZipEntryCompressor fix; Pakko's own .NET-based extraction
        // reported no error at all, so only an independent reader like 7za.exe can catch a
        // regression here.
        if (!SevenZipRunner.IsAvailable) return; // defense-in-depth only, see SevenZipRunner

        string sourceDir = Path.Combine(_temp.Path, "source");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(Path.Combine(sourceDir, "empty-subfolder"));

        for (int i = 0; i < 70; i++)
            File.WriteAllText(Path.Combine(sourceDir, $"file{i}.txt"), $"content {i}");

        File.WriteAllBytes(Path.Combine(sourceDir, ".gitkeep"), []);
        File.WriteAllBytes(Path.Combine(sourceDir, "lockfile"), []);
        File.WriteAllBytes(Path.Combine(sourceDir, "gc.properties"), []);

        string destinationDir = Path.Combine(_temp.Path, "dest");
        Directory.CreateDirectory(destinationDir);

        var service = new Archiver.Core.Services.ZipArchiveService();
        var result = await service.ArchiveAsync(new Archiver.Core.Models.ArchiveOptions
        {
            SourcePaths = [sourceDir],
            DestinationFolder = destinationDir,
            ArchiveName = "repro",
            CompressionLevel = CompressionLevel.Optimal,
        }, progress: null, CancellationToken.None);

        result.Errors.Should().BeEmpty();
        string archivePath = Path.Combine(destinationDir, "repro.zip");
        File.Exists(archivePath).Should().BeTrue();

        var act = () => SevenZipRunner.Test(archivePath);
        act.Should().NotThrow("a real folder with genuinely empty files/folders must produce a spec-compliant archive, not just one .NET's own lenient reader accepts");
    }

    private async Task<(string ArchivePath, Dictionary<string, byte[]?> Expected)> BuildMixedArchiveAsync()
    {
        string archivePath = Path.Combine(_temp.Path, "mixed.zip");
        var expected = new Dictionary<string, byte[]?>();

        await using (var writer = new ZipEntryWriter(archivePath))
        {
            // Small file, compressed fully in memory (the parallel-eligible path).
            byte[] smallContent = System.Text.Encoding.UTF8.GetBytes("hello small file, compressed in memory");
            using (var ms = new MemoryStream(smallContent))
            {
                var compressed = ZipEntryCompressor.Compress(ms, CompressionLevel.Optimal);
                await writer.WriteCompressedEntryAsync("small.txt", compressed, DateTime.UtcNow, CancellationToken.None);
            }
            expected["small.txt"] = smallContent;

            // NoCompression ("Stored" method) entry, same in-memory path.
            byte[] storedContent = System.Text.Encoding.UTF8.GetBytes("stored, no compression");
            using (var ms = new MemoryStream(storedContent))
            {
                var compressed = ZipEntryCompressor.Compress(ms, CompressionLevel.NoCompression);
                await writer.WriteCompressedEntryAsync("stored.txt", compressed, DateTime.UtcNow, CancellationToken.None);
            }
            expected["stored.txt"] = storedContent;

            // Cyrillic + emoji name (UTF-8 general-purpose flag bit).
            byte[] cyrillicContent = System.Text.Encoding.UTF8.GetBytes("кирилиця та emoji 📦");
            using (var ms = new MemoryStream(cyrillicContent))
            {
                var compressed = ZipEntryCompressor.Compress(ms, CompressionLevel.Optimal);
                await writer.WriteCompressedEntryAsync("приклад_📦.txt", compressed, DateTime.UtcNow, CancellationToken.None);
            }
            expected["приклад_📦.txt"] = cyrillicContent;

            // Directory placeholder (T-F66 equivalent).
            await writer.WriteDirectoryPlaceholderAsync("empty-folder/", DateTime.UtcNow, CancellationToken.None);
            expected["empty-folder/"] = null;

            // Zero-byte real file compressed at a non-Stored level, same in-memory path.
            // DeflateStream never writes anything for zero input (0 output bytes, not even a
            // minimal valid final block), so tagging it Deflate produces an entry real deflate
            // readers reject as corrupt even though .NET's own reader accepts it silently —
            // caught via a real on-device 7-Zip/NanaZip extraction failure. Must round-trip as
            // Stored regardless of the requested level; see ZipEntryCompressor.Compress.
            using (var ms = new MemoryStream())
            {
                var compressed = ZipEntryCompressor.Compress(ms, CompressionLevel.Optimal);
                await writer.WriteCompressedEntryAsync("empty.txt", compressed, DateTime.UtcNow, CancellationToken.None);
            }
            expected["empty.txt"] = Array.Empty<byte>();

            // Larger file through the temp-file-compressed path (T-F35 follow-up — replaces the
            // old single-threaded "streamed passthrough" design; see DECISIONS.md).
            byte[] largeContent = BuildSemiCompressibleContent(512 * 1024);
            using (var sourceMs = new MemoryStream(largeContent))
            {
                var compressed = ZipEntryCompressor.Compress(sourceMs, CompressionLevel.Optimal);
                using var compressedMs = new MemoryStream(compressed.CompressedBytes);
                await writer.WriteCompressedEntryFromStreamAsync(
                    "streamed.bin", compressedMs, compressed.CompressedBytes.Length, compressed.UncompressedLength,
                    compressed.Crc32, compressed.Method, DateTime.UtcNow, CancellationToken.None);
            }
            expected["streamed.bin"] = largeContent;
        }

        return (archivePath, expected);
    }

    private static byte[] BuildSemiCompressibleContent(int length)
    {
        var rng = new Random(20260718);
        var block = new byte[64 * 1024];
        rng.NextBytes(block);
        var result = new byte[length];
        for (int offset = 0; offset < length; offset += block.Length)
        {
            int count = Math.Min(block.Length, length - offset);
            Array.Copy(block, 0, result, offset, count);
        }
        return result;
    }

    // Deliberately independent of ZipEntryWriter's own code — reads raw bytes directly per the
    // ZIP spec, so a bug shared between writer and this parser wouldn't silently agree with itself.
    private sealed record RawZipStructure(IReadOnlyList<RawCentralDirectoryRecord> CentralDirectoryRecords)
    {
        public static RawZipStructure Parse(byte[] bytes)
        {
            // No archive built by this test suite ever writes a comment, so the traditional EOCD
            // record is always exactly the last 22 bytes.
            int eocdOffset = bytes.Length - 22;
            BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(eocdOffset, 4)).Should().Be(0x06054b50u);

            ushort entryCount = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(eocdOffset + 10, 2));
            uint centralDirSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(eocdOffset + 12, 4));
            uint centralDirOffset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(eocdOffset + 16, 4));

            long realEntryCount = entryCount;
            long realCentralDirOffset = centralDirOffset;

            if (entryCount == 0xFFFF || centralDirOffset == 0xFFFFFFFF)
            {
                int locatorOffset = eocdOffset - 20;
                BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(locatorOffset, 4)).Should().Be(0x07064b50u);
                long zip64EocdOffset = (long)BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(locatorOffset + 8, 8));

                BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan((int)zip64EocdOffset, 4)).Should().Be(0x06064b50u);
                realEntryCount = (long)BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan((int)zip64EocdOffset + 32, 8));
                realCentralDirOffset = (long)BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan((int)zip64EocdOffset + 48, 8));
            }

            var records = new List<RawCentralDirectoryRecord>();
            long pos = realCentralDirOffset;
            for (int i = 0; i < realEntryCount; i++)
            {
                BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan((int)pos, 4)).Should().Be(0x02014b50u);
                uint compressedSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan((int)pos + 20, 4));
                ushort nameLen = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan((int)pos + 28, 2));
                ushort extraLen = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan((int)pos + 30, 2));
                ushort commentLen = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan((int)pos + 32, 2));
                uint localHeaderOffset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan((int)pos + 42, 4));
                string name = System.Text.Encoding.UTF8.GetString(bytes, (int)pos + 46, nameLen);

                long realLocalHeaderOffset = localHeaderOffset;
                if (localHeaderOffset == 0xFFFFFFFF)
                {
                    // Zip64 extra field sub-fields appear in fixed order: uncompressed, compressed,
                    // local header offset, disk start — only those whose 32-bit field was marked.
                    int extraStart = (int)pos + 46 + nameLen;
                    int sub = extraStart + 4; // skip tag+size
                    if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan((int)pos + 24, 4)) == 0xFFFFFFFF) sub += 8; // uncompressed marked
                    if (compressedSize == 0xFFFFFFFF) sub += 8; // compressed marked
                    realLocalHeaderOffset = (long)BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(sub, 8));
                }

                records.Add(new RawCentralDirectoryRecord(name, realLocalHeaderOffset));
                pos += 46 + nameLen + extraLen + commentLen;
            }

            return new RawZipStructure(records);
        }
    }

    private sealed record RawCentralDirectoryRecord(string Name, long LocalHeaderOffset);
}
