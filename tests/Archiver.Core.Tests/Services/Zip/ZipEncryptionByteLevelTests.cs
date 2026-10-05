using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services.Zip;

/// <summary>
/// T-F337: the encrypted ZIPs <see cref="ZipArchiveService.ArchiveAsync"/> writes, checked byte by
/// byte with a reader written here from the WinZip AE-2 specification. Nothing below goes through
/// Pakko's own ZIP or AES code (no <c>Archiver.Core.Services.Zip</c> type is used), so a bug shared
/// by Pakko's writer and reader cannot hide the way it can in a round trip. 7-Zip reading the same
/// kind of archive is the second independent reader (ZipEncryptionCompatibilityTests).
/// </summary>
public sealed class ZipEncryptionByteLevelTests : IDisposable
{
    private const string Password = "Corr3ct Horse~!";
    private const string Marker = "PAKKO-PLAINTEXT-MARKER-";
    private const int AboveInMemoryLimit = 1024 * 1024 + 17;
    private const int SmallFileCount = 70;

    private static readonly int[] BoundarySizes = [0, 1, 15, 16, 17, 65536, 65537];

    private readonly ZipArchiveService _sut = new(new GroupPolicyOptions());
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private sealed record RawEntry(
        string Name, ushort VersionNeeded, ushort LocalVersionNeeded, ushort Flags, ushort Method, uint Crc32,
        uint UncompressedSize, bool LocalMatchesCentral, byte[]? AesExtra, byte[]? LocalAesExtra, byte[] Data);

    // ── The independent reader ───────────────────────────────────────────────

    private static ushort U16(byte[] zip, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(offset, 2));

    private static uint U32(byte[] zip, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(offset, 4));

    private static byte[]? FindExtra(byte[] zip, int start, int length, ushort wantedTag)
    {
        int end = start + length;
        for (int i = start; i + 4 <= end;)
        {
            ushort tag = U16(zip, i);
            ushort size = U16(zip, i + 2);
            if (tag == wantedTag)
                return zip.AsSpan(i + 4, size).ToArray();
            i += 4 + size;
        }
        return null;
    }

    private static List<RawEntry> ParseArchive(byte[] zip)
    {
        int eocd = zip.AsSpan().LastIndexOf("PK\u0005\u0006"u8);
        eocd.Should().BeGreaterThanOrEqualTo(0, "the archive ends with an end-of-central-directory record");
        int count = U16(zip, eocd + 10);
        int position = (int)U32(zip, eocd + 16);

        var entries = new List<RawEntry>(count);
        for (int n = 0; n < count; n++)
        {
            U32(zip, position).Should().Be(0x02014B50u, "central directory record signature");
            int nameLength = U16(zip, position + 28);
            int extraLength = U16(zip, position + 30);
            int commentLength = U16(zip, position + 32);
            int local = (int)U32(zip, position + 42);
            U32(zip, local).Should().Be(0x04034B50u, "local file header signature");
            int localNameLength = U16(zip, local + 26);
            int localExtraLength = U16(zip, local + 28);
            uint compressedSize = U32(zip, position + 20);

            // flags, method, CRC-32, both sizes and the name, each read from both headers
            bool same = U16(zip, position + 8) == U16(zip, local + 6)
                && U16(zip, position + 10) == U16(zip, local + 8)
                && zip.AsSpan(position + 16, 12).SequenceEqual(zip.AsSpan(local + 14, 12))
                && zip.AsSpan(position + 46, nameLength).SequenceEqual(zip.AsSpan(local + 30, localNameLength));

            entries.Add(new RawEntry(
                Name: Encoding.UTF8.GetString(zip, position + 46, nameLength),
                VersionNeeded: U16(zip, position + 6),
                LocalVersionNeeded: U16(zip, local + 4),
                Flags: U16(zip, position + 8),
                Method: U16(zip, position + 10),
                Crc32: U32(zip, position + 16),
                UncompressedSize: U32(zip, position + 24),
                LocalMatchesCentral: same,
                AesExtra: FindExtra(zip, position + 46 + nameLength, extraLength, 0x9901),
                LocalAesExtra: FindExtra(zip, local + 30 + localNameLength, localExtraLength, 0x9901),
                Data: zip.AsSpan(local + 30 + localNameLength + localExtraLength, (int)compressedSize).ToArray()));
            position += 46 + nameLength + extraLength + commentLength;
        }
        return entries;
    }

    // WinZip AES is AES in counter mode with a 16-byte little-endian counter that starts at 1.
    private static byte[] DecryptCtr(byte[] key, byte[] ciphertext)
    {
        int blocks = (ciphertext.Length + 15) / 16;
        byte[] counters = new byte[blocks * 16];
        for (int block = 0; block < blocks; block++)
            BinaryPrimitives.WriteInt64LittleEndian(counters.AsSpan(block * 16, 8), block + 1L);

        using var aes = Aes.Create();
        aes.Key = key;
        byte[] keystream = aes.EncryptEcb(counters, PaddingMode.None);

        byte[] plain = new byte[ciphertext.Length];
        for (int i = 0; i < plain.Length; i++)
            plain[i] = (byte)(ciphertext[i] ^ keystream[i]);
        return plain;
    }

    private static byte[] Inflate(byte[] deflated)
    {
        using var inflater = new DeflateStream(new MemoryStream(deflated), CompressionMode.Decompress);
        using var output = new MemoryStream();
        inflater.CopyTo(output);
        return output.ToArray();
    }

    /// <summary>Checks one file entry against the AE-2 layout and returns its salt and real method.</summary>
    private static (byte[] Salt, ushort RealMethod) VerifyFileEntry(RawEntry entry, byte[] source)
    {
        string name = entry.Name;
        entry.LocalMatchesCentral.Should().BeTrue($"local and central headers of '{name}' agree");
        (entry.Flags & 0x0001).Should().Be(1, $"'{name}' has the encrypted flag");
        (entry.Flags & 0x0008).Should().Be(0, $"'{name}' has no data descriptor");
        (entry.Flags & 0x0040).Should().Be(0, $"'{name}' does not claim PKWARE strong encryption");
        entry.Method.Should().Be(99, $"'{name}' is a WinZip AES entry");
        entry.Crc32.Should().Be(0u, $"AE-2 hides the CRC-32 of '{name}'");
        entry.VersionNeeded.Should().Be(51, name);
        entry.LocalVersionNeeded.Should().Be(51, name);
        entry.UncompressedSize.Should().Be((uint)source.Length, name);

        entry.AesExtra.Should().NotBeNull($"'{name}' carries the 0x9901 extra field");
        byte[] extra = entry.AesExtra!;
        entry.LocalAesExtra.Should().Equal(extra, $"both headers of '{name}' carry the same AES extra field");
        extra.Should().HaveCount(7, name);
        BinaryPrimitives.ReadUInt16LittleEndian(extra).Should().Be(2, $"'{name}' is AE-2");
        extra.AsSpan(2, 2).ToArray().Should().Equal("AE"u8.ToArray(), name);
        extra[4].Should().Be(3, $"'{name}' uses AES-256");
        ushort realMethod = BinaryPrimitives.ReadUInt16LittleEndian(extra.AsSpan(5));
        realMethod.Should().BeOneOf([(ushort)0, (ushort)8], name);

        entry.Data.Length.Should().BeGreaterThanOrEqualTo(16 + 2 + 10, $"'{name}' holds a salt, a verification value and a code");
        byte[] salt = entry.Data[..16];
        byte[] verification = entry.Data[16..18];
        byte[] ciphertext = entry.Data[18..^10];
        byte[] code = entry.Data[^10..];

        // PBKDF2-HMAC-SHA1 with 1000 iterations and HMAC-SHA1 are fixed by the WinZip AE specification.
#pragma warning disable CA5379, CA5387, CA5350
        byte[] derived = Rfc2898DeriveBytes.Pbkdf2(Encoding.ASCII.GetBytes(Password), salt, 1000, HashAlgorithmName.SHA1, 66);
        byte[] expectedCode = HMACSHA1.HashData(derived[32..64], ciphertext)[..10];
#pragma warning restore CA5379, CA5387, CA5350
        verification.Should().Equal(derived[64..66], $"password verification value of '{name}'");
        code.Should().Equal(expectedCode, $"the authentication code of '{name}' is HMAC-SHA1 over its ciphertext");

        byte[] decrypted = DecryptCtr(derived[..32], ciphertext);
        byte[] plain = realMethod == 0 ? decrypted : Inflate(decrypted);
        plain.AsSpan().SequenceEqual(source).Should().BeTrue($"'{name}' decrypts to its source bytes");
        return (salt, realMethod);
    }

    // ── Fixture ──────────────────────────────────────────────────────────────

    // Every file repeats the marker, so its content compresses well and would be easy to find in
    // an archive that stored it unencrypted.
    private static byte[] Content(int size, int seed)
    {
        byte[] unit = Encoding.ASCII.GetBytes($"{Marker}{seed:D6}\n");
        byte[] content = new byte[size];
        for (int i = 0; i < size; i++)
            content[i] = unit[i % unit.Length];
        return content;
    }

    // src/ { s<size>.bin for each boundary size, big.bin (temp-file path), many/f000..f069.txt, empty/ }
    private string CreateSourceTree()
    {
        string root = Path.Combine(_temp.Path, "src");
        Directory.CreateDirectory(Path.Combine(root, "many"));
        Directory.CreateDirectory(Path.Combine(root, "empty"));
        foreach (int size in BoundarySizes)
            File.WriteAllBytes(Path.Combine(root, $"s{size}.bin"), Content(size, size));
        File.WriteAllBytes(Path.Combine(root, "big.bin"), Content(AboveInMemoryLimit, 1));
        for (int i = 0; i < SmallFileCount; i++)
            File.WriteAllBytes(Path.Combine(root, "many", $"f{i:D3}.txt"), Content(200 + i, 1000 + i));
        return root;
    }

    private async Task<byte[]> ArchiveAsync(string source, string outputName, CompressionLevel level)
    {
        ArchiveResult result = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [source],
            DestinationFolder = Path.Combine(_temp.Path, outputName),
            ArchiveName = "out",
            CompressionLevel = level,
            ResolvePasswordAsync = _ => Task.FromResult(new PasswordDecision { Password = Password }),
        });
        result.Success.Should().BeTrue(because: string.Join("; ", result.Errors.Select(e => e.Message)));
        return await File.ReadAllBytesAsync(result.CreatedFiles.Should().ContainSingle().Subject);
    }

    private static byte[] SourceOf(string sourceRoot, string entryName) =>
        File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(sourceRoot)!, entryName.Replace('/', Path.DirectorySeparatorChar)));

    // ── Tests ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(CompressionLevel.Optimal)]
    [InlineData(CompressionLevel.NoCompression)]
    public async Task ArchiveAsync_WithPassword_EveryEntryIsValidAe2ByAnIndependentReader(CompressionLevel level)
    {
        string source = CreateSourceTree();

        byte[] zip = await ArchiveAsync(source, "out", level);

        List<RawEntry> entries = ParseArchive(zip);
        List<RawEntry> files = entries.Where(e => !e.Name.EndsWith('/')).ToList();
        files.Should().HaveCount(BoundarySizes.Length + 1 + SmallFileCount);
        foreach (RawEntry folder in entries.Where(e => e.Name.EndsWith('/')))
        {
            folder.Method.Should().Be(0, folder.Name);
            (folder.Flags & 0x0001).Should().Be(0, $"folder entry '{folder.Name}' is not encrypted");
            folder.Data.Should().BeEmpty(folder.Name);
        }

        var salts = new List<string>();
        foreach (RawEntry file in files)
        {
            byte[] sourceBytes = SourceOf(source, file.Name);
            (byte[] salt, ushort realMethod) = VerifyFileEntry(file, sourceBytes);
            salts.Add(Convert.ToHexString(salt));

            // T-F299 stores an entry Deflate did not shrink, so only the well-compressing sizes
            // say which method the level gives.
            if (level == CompressionLevel.NoCompression || sourceBytes.Length == 0)
                realMethod.Should().Be(0, file.Name);
            else if (sourceBytes.Length >= 200)
                realMethod.Should().Be(8, file.Name);
            if (sourceBytes.Length == 0)
                file.Data.Should().HaveCount(16 + 2 + 10, "an empty file is its salt, verification value and code only");
        }

        salts.Should().OnlyHaveUniqueItems("every entry needs its own salt");
        salts.Should().NotContain(new string('0', 32));
        zip.AsSpan().IndexOf(Encoding.ASCII.GetBytes(Marker)).Should().Be(-1, "no source bytes are readable in the archive");
    }

    [Fact]
    public async Task ArchiveAsync_WithPassword_TwoRunsOverTheSameInputShareNoSalt()
    {
        string source = CreateSourceTree();

        byte[] first = await ArchiveAsync(source, "one", CompressionLevel.Optimal);
        byte[] second = await ArchiveAsync(source, "two", CompressionLevel.Optimal);

        static List<string> Salts(byte[] zip) => ParseArchive(zip)
            .Where(e => !e.Name.EndsWith('/'))
            .Select(e => Convert.ToHexString(e.Data.AsSpan(0, 16)))
            .ToList();
        Salts(first).Should().NotIntersectWith(Salts(second));
    }
}
