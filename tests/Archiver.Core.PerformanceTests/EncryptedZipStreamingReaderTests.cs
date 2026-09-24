using System.Security.Cryptography;
using Archiver.Core.Services.Zip.Decryption;
using FluentAssertions;

namespace Archiver.Core.PerformanceTests;

/// <summary>
/// T-F193 phase 1 (user decision 2026-09-24: streaming two-pass reader instead of a size cap).
/// EncryptedZipEntryReader used to buffer the whole ciphertext AND the whole decrypted payload in
/// memory, which capped readable encrypted entries at ~2 GiB and made every large entry cost
/// 2x its size in RAM. Pass 1 now authenticates the ciphertext straight from disk (HMAC only, no
/// plaintext); pass 2 decrypts as the caller reads. Uses the vendored 7za as the independent
/// source of real AES entries.
/// </summary>
[Trait("Category", "Slow")]
public sealed class EncryptedZipStreamingReaderTests : IDisposable
{
    private const string Password = "testpassword";
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private (string ZipPath, byte[] ExpectedSha256, long Size) BuildRandomAesEntry(int megabytes)
    {
        string source = Path.Combine(_temp.Path, "payload.bin");
        byte[] chunk = new byte[1024 * 1024];
        using (var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        using (var file = File.Create(source))
        {
            for (int i = 0; i < megabytes; i++)
            {
                RandomNumberGenerator.Fill(chunk);
                sha.AppendData(chunk);
                file.Write(chunk);
            }
            string zipPath = Path.Combine(_temp.Path, "big.zip");
            file.Dispose();
            SevenZipRunner.ArchiveEncrypted(zipPath, Password, store: true, source);
            return (zipPath, sha.GetHashAndReset(), (long)megabytes * chunk.Length);
        }
    }

    [Fact]
    public void TryOpen_LargeAesEntry_StreamsWithoutBufferingTheEntry()
    {
        var (zipPath, expectedSha256, size) = BuildRandomAesEntry(megabytes: 64);

        using var fs = File.OpenRead(zipPath);
        var located = RawZipEntryLocator.LocateAll(fs).Single();

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var (result, content) = EncryptedZipEntryReader.TryOpen(fs, located, Password);
        result.Should().Be(EncryptedZipReadResult.Success);

        byte[] buffer = new byte[81920];
        long total = 0;
        using (content)
        using (var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            int read;
            while ((read = content!.Read(buffer, 0, buffer.Length)) > 0)
            {
                sha.AppendData(buffer, 0, read);
                total += read;
            }
            sha.GetHashAndReset().Should().Equal(expectedSha256);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        total.Should().Be(size);
        allocated.Should().BeLessThan(8L * 1024 * 1024,
            "a 64 MiB entry must be authenticated and decrypted in bounded memory, not buffered whole");
    }

    // The pre-T-F193 reader refused any entry >= int.MaxValue bytes outright (it had to fit one
    // byte[]). On demand only: ~2.2 GB of random data, ~4.4 GB of disk while it runs.
    [Fact]
    [Trait("Category", "VeryLarge")]
    public void TryOpen_AesEntryLargerThanIntMaxValue_ReadsBackByteExact()
    {
        var (zipPath, expectedSha256, size) = BuildRandomAesEntry(megabytes: 2100);

        using var fs = File.OpenRead(zipPath);
        var located = RawZipEntryLocator.LocateAll(fs).Single();
        located.CompressedSize.Should().BeGreaterThan(int.MaxValue);

        var (result, content) = EncryptedZipEntryReader.TryOpen(fs, located, Password);
        result.Should().Be(EncryptedZipReadResult.Success);

        byte[] buffer = new byte[1024 * 1024];
        long total = 0;
        using (content)
        using (var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            int read;
            while ((read = content!.Read(buffer, 0, buffer.Length)) > 0)
            {
                sha.AppendData(buffer, 0, read);
                total += read;
            }
            sha.GetHashAndReset().Should().Equal(expectedSha256);
        }
        total.Should().Be(size);
    }
}
