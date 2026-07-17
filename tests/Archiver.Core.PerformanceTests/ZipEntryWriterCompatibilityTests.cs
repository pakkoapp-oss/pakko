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
    public async Task WriteStreamedEntryAsync_LargeSizeHintForcesZip64_StillProducesValidArchive()
    {
        string archivePath = Path.Combine(_temp.Path, "zip64-forced.zip");
        string sourceFile = _temp.CreateFile("small-but-hinted-large.bin", "tiny real content, huge hint");

        await using (var writer = new ZipEntryWriter(archivePath))
        {
            // Passes a size hint far above the Zip64 threshold while the real file is tiny —
            // forces the Zip64 local-header/extra-field/patch-back code path to run without
            // needing gigabytes of real test data on disk.
            await writer.WriteStreamedEntryAsync(
                sourceFile, "forced.bin", CompressionLevel.Optimal,
                uncompressedLengthHint: 5_000_000_000L, DateTime.UtcNow,
                progress: null, totalBytes: 0, startOffset: 0, CancellationToken.None);
        }

        byte[] bytes = await File.ReadAllBytesAsync(archivePath);
        RawZipStructure structure = RawZipStructure.Parse(bytes);
        structure.CentralDirectoryRecords.Should().ContainSingle();

        using var archive = ZipFile.OpenRead(archivePath);
        var entry = archive.Entries.Should().ContainSingle().Subject;
        using var reader = new StreamReader(entry.Open());
        (await reader.ReadToEndAsync()).Should().Be("tiny real content, huge hint");

        if (SevenZipRunner.IsAvailable)
            SevenZipRunner.Test(archivePath); // throws if the forced-Zip64 bytes are actually invalid
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

            // Larger file through the streamed passthrough path.
            string largeSourcePath = Path.Combine(_temp.Path, "streamed-source.bin");
            byte[] largeContent = BuildSemiCompressibleContent(512 * 1024);
            await File.WriteAllBytesAsync(largeSourcePath, largeContent);
            await writer.WriteStreamedEntryAsync(
                largeSourcePath, "streamed.bin", CompressionLevel.Optimal,
                uncompressedLengthHint: largeContent.Length, DateTime.UtcNow,
                progress: null, totalBytes: largeContent.Length, startOffset: 0, CancellationToken.None);
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
