using System.IO.Compression;
using System.Text;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F246: extraction only copied the entry stream — .NET does not check CRC-32 on read — so a
// corrupted entry was written and reported as success. T-F231: a decrypted entry was not capped
// at its declared size, which the compression-bomb and free-space gates rely on.
public sealed class ZipArchiveServiceExtractIntegrityTests : IDisposable
{
    private const string Password = "s3cret-pass";
    private static readonly string LongContent = string.Concat(Enumerable.Repeat("integrity-check ", 64));

    private readonly ZipArchiveService _sut = new();
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string CreateZip(string name, CompressionLevel level)
    {
        string zipPath = Path.Combine(_temp.Path, name);
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (string entry in new[] { "ok.txt", "doc.txt" })
        {
            using var w = new StreamWriter(archive.CreateEntry(entry, level).Open());
            w.Write(entry == "doc.txt" ? LongContent : "fine");
        }
        return zipPath;
    }

    // Offsets of doc.txt's local header and central directory record.
    private static (int Local, int Central) FindHeaders(byte[] bytes, string entryName)
    {
        byte[] name = Encoding.UTF8.GetBytes(entryName);
        int local = -1, central = -1;
        for (int i = 0; i + 4 <= bytes.Length; i++)
        {
            uint sig = BitConverter.ToUInt32(bytes, i);
            if (sig == 0x04034b50 && i + 30 + name.Length <= bytes.Length && bytes.AsSpan(i + 30, name.Length).SequenceEqual(name))
                local = i;
            else if (sig == 0x02014b50 && i + 46 + name.Length <= bytes.Length && bytes.AsSpan(i + 46, name.Length).SequenceEqual(name))
                central = i;
        }
        local.Should().BeGreaterThanOrEqualTo(0);
        central.Should().BeGreaterThanOrEqualTo(0);
        return (local, central);
    }

    private static void PatchUInt32(string zipPath, string entryName, int localField, int centralField, uint value)
    {
        byte[] bytes = File.ReadAllBytes(zipPath);
        var (local, central) = FindHeaders(bytes, entryName);
        BitConverter.GetBytes(value).CopyTo(bytes, local + localField);
        BitConverter.GetBytes(value).CopyTo(bytes, central + centralField);
        File.WriteAllBytes(zipPath, bytes);
    }

    private static void PatchCrc(string zipPath, string entryName, uint crc) => PatchUInt32(zipPath, entryName, 14, 16, crc);

    private static void PatchUncompressedSize(string zipPath, string entryName, uint size) => PatchUInt32(zipPath, entryName, 22, 24, size);

    private async Task<(ArchiveResult Result, string Dest)> ExtractAsync(string zip, string? password = null)
    {
        string dest = Path.Combine(_temp.Path, "out");
        Directory.CreateDirectory(dest);
        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [zip],
            DestinationFolder = dest,
            Mode = ExtractMode.SingleFolder,
            ResolvePasswordAsync = password is null ? null : _ => Task.FromResult(new PasswordDecision { Password = password }),
        });
        return (result, dest);
    }

    private static void AssertOnlyDocFailed(ArchiveResult result, string dest, string expectedFragment)
    {
        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Message.Should().Contain("doc.txt").And.Contain(expectedFragment);
        File.Exists(Path.Combine(dest, "doc.txt")).Should().BeFalse("a corrupted entry must never reach the destination");
        File.ReadAllText(Path.Combine(dest, "ok.txt")).Should().Be("fine");
        result.Sources.Should().ContainSingle().Which.Outcome.Should().Be(SourceOutcome.Partial);
        Directory.GetDirectories(dest, ".pakko-x-*").Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractAsync_StoredEntryWithOneFlippedByte_FailsOnlyThatEntry()
    {
        string zip = CreateZip("stored.zip", CompressionLevel.NoCompression);
        byte[] bytes = File.ReadAllBytes(zip);
        int dataAt = bytes.AsSpan().IndexOf(Encoding.ASCII.GetBytes(LongContent[..32]));
        dataAt.Should().BeGreaterThan(0);
        bytes[dataAt + 5] ^= 0x01;
        File.WriteAllBytes(zip, bytes);

        var (result, dest) = await ExtractAsync(zip);

        AssertOnlyDocFailed(result, dest, "CRC-32");
    }

    [Fact]
    public async Task ExtractAsync_DeflatedEntryWithWrongCrc_FailsOnlyThatEntry()
    {
        string zip = CreateZip("deflated.zip", CompressionLevel.Optimal);
        PatchCrc(zip, "doc.txt", 0xDEADBEEF);

        var (result, dest) = await ExtractAsync(zip);

        AssertOnlyDocFailed(result, dest, "CRC-32");
    }

    // Stored: .NET returns every stored byte, so the length cap fires. Deflated: .NET silently
    // truncates to the declared size, so the CRC of the full content no longer matches.
    [Theory]
    [InlineData(CompressionLevel.NoCompression, "declared size")]
    [InlineData(CompressionLevel.Optimal, "CRC-32")]
    public async Task ExtractAsync_UnderstatedUncompressedSize_FailsOnlyThatEntry(CompressionLevel level, string expected)
    {
        string zip = CreateZip("under.zip", level);
        PatchUncompressedSize(zip, "doc.txt", 10);

        var (result, dest) = await ExtractAsync(zip);

        AssertOnlyDocFailed(result, dest, expected);
    }

    // T-F231: AE-2 has no CRC (HMAC covers the ciphertext only), so a declared size smaller than
    // the real content is caught only by capping the output.
    [Fact]
    public async Task ExtractAsync_Ae2EntryLargerThanDeclared_FailsOnlyThatEntry()
    {
        string src = Path.Combine(_temp.Path, "src");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "ok.txt"), "fine");
        File.WriteAllText(Path.Combine(src, "doc.txt"), LongContent);
        var created = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [Path.Combine(src, "ok.txt"), Path.Combine(src, "doc.txt")],
            DestinationFolder = _temp.Path,
            ArchiveName = "enc",
            Mode = ArchiveMode.SingleArchive,
            ResolvePasswordAsync = _ => Task.FromResult(new PasswordDecision { Password = Password }),
        });
        created.Success.Should().BeTrue();
        string zip = Path.Combine(_temp.Path, "enc.zip");
        PatchUncompressedSize(zip, "doc.txt", 10);

        var (result, dest) = await ExtractAsync(zip, Password);

        AssertOnlyDocFailed(result, dest, "declared size");
    }
}
