using System.IO.Compression;
using Archiver.Core.Services.Zip;
using Archiver.Core.Services.Zip.Decryption;
using FluentAssertions;

namespace Archiver.Core.Tests.Services.Zip;

// T-F504: the AES streams sized their buffers for 64 KiB whatever the entry (three of them writing,
// two and an 80 KiB HMAC buffer reading), about 200-300 KB per entry: 4.2 GB allocated for 20,000
// small files. They now take the entry's length as a hint; the keystream bytes do not depend on it.
public sealed class WinZipAesAllocationTests
{
    private const string Password = "allocation-test";
    private const long SmallEntryBudget = 24 * 1024;

    [Fact]
    public void Encrypt_SmallEntry_AllocatesForTheEntryNotFor64KiB()
    {
        byte[] plain = Payload(1000);
        Encrypt(plain); // warm-up: first-call allocations of the crypto stack are not the entry's

        long before = GC.GetAllocatedBytesForCurrentThread();
        Encrypt(plain);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.Should().BeLessThan(SmallEntryBudget);
    }

    [Fact]
    public void Decrypt_SmallEntry_AllocatesForTheEntryNotFor64KiB()
    {
        byte[] cipher = Encrypt(Payload(1000));
        Decrypt(cipher);

        long before = GC.GetAllocatedBytesForCurrentThread();
        Decrypt(cipher);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.Should().BeLessThan(SmallEntryBudget);
    }

    // Sizes around the keystream's 16-byte block and 64 KiB batch: the hint changes buffer sizes only.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(65535)]
    [InlineData(65536)]
    [InlineData(65537)]
    [InlineData(300_000)]
    public void EncryptThenDecrypt_AnySize_RoundTrips(int size)
    {
        byte[] plain = Payload(size);

        Decrypt(Encrypt(plain)).Should().Equal(plain);
    }

    // A hint smaller than what arrives (a stream that grew, a wrong size) refills as before.
    [Fact]
    public void Keystream_MoreBytesThanTheHint_SameBytesAsWithoutAHint()
    {
        byte[] key = new byte[32];
        new Random(3).NextBytes(key);
        byte[] input = Payload(100_000);
        byte[] hinted = new byte[input.Length];
        byte[] plainKeystream = new byte[input.Length];

        using (var small = new AesCtrKeystream(key, expectedBytes: 20))
            small.Apply(input, hinted);
        using (var full = new AesCtrKeystream(key))
            full.Apply(input, plainKeystream);

        hinted.Should().Equal(plainKeystream);
    }

    private static byte[] Payload(int size)
    {
        byte[] bytes = new byte[size];
        new Random(size).NextBytes(bytes);
        return bytes;
    }

    private static byte[] Encrypt(byte[] plain)
    {
        using var output = new MemoryStream(plain.Length + WinZipAesEncryptStream.Overhead);
        using (var aes = new WinZipAesEncryptStream(output, Password, plain.Length))
            aes.Write(plain);
        return output.ToArray();
    }

    private static byte[] Decrypt(byte[] encrypted)
    {
        int saltLength = WinZipAesReader.SaltLength(WinZipAesEncryptStream.StrengthBits);
        byte[] salt = encrypted[..saltLength];
        (byte[] encryptionKey, byte[] authenticationKey, byte[] _) =
            WinZipAesReader.DeriveKeys(Password, salt, WinZipAesEncryptStream.StrengthBits);
        int headerLength = saltLength + WinZipAesReader.PasswordVerificationLength;
        int cipherLength = encrypted.Length - headerLength - WinZipAesReader.AuthenticationCodeLength;
        byte[] tag = encrypted[^WinZipAesReader.AuthenticationCodeLength..];

        using (var pass1 = new MemoryStream(encrypted, headerLength, cipherLength, writable: false))
            WinZipAesReader.Authenticate(pass1, authenticationKey, tag, cipherLength).Should().BeTrue();
        byte[] plain = new byte[cipherLength];
        using var pass2 = new WinZipAesCtrStream(new MemoryStream(encrypted, headerLength, cipherLength, writable: false), encryptionKey, cipherLength);
        pass2.ReadExactly(plain);
        return plain;
    }
}
