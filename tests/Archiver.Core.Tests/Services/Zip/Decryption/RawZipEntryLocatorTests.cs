using Archiver.Core.Services.Zip.Decryption;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services.Zip.Decryption;

public sealed class RawZipEntryLocatorTests
{
    [Fact]
    public void Locate_ZipCryptoEntry_ReportsCorrectMethodAndEncryptedBit()
    {
        using var fs = File.OpenRead(FixtureHelper.Archive("encrypted_zipcrypto_real.zip"));

        var located = RawZipEntryLocator.Locate(fs, "compressible.txt");

        located.GeneralPurposeEncryptedBit.Should().BeTrue();
        located.CompressionMethod.Should().Be(8); // Deflate
        located.AeVersion.Should().BeNull();
        located.CompressedSize.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Locate_Aes256Entry_ReportsAe2AndStrength256()
    {
        using var fs = File.OpenRead(FixtureHelper.Archive("encrypted_aes256.zip"));

        var located = RawZipEntryLocator.Locate(fs, "compressible.txt");

        located.GeneralPurposeEncryptedBit.Should().BeTrue();
        located.CompressionMethod.Should().Be(99); // WinZip AES
        located.AeVersion.Should().Be(2);
        located.AesStrengthBits.Should().Be(256);
        located.RealCompressionMethod.Should().Be(8); // Deflate under AES
        located.StoredCrc32.Should().Be(0); // AE-2 zeroes the header CRC-32
    }

    [Fact]
    public void Locate_Aes128Entry_ReportsAe2AndStrength128()
    {
        using var fs = File.OpenRead(FixtureHelper.Archive("encrypted_aes128.zip"));

        var located = RawZipEntryLocator.Locate(fs, "compressible.txt");

        located.AeVersion.Should().Be(2);
        located.AesStrengthBits.Should().Be(128);
    }

    [Fact]
    public void Locate_SyntheticAe1Fixture_ReportsAe1AndRealCrc()
    {
        using var fs = File.OpenRead(FixtureHelper.Archive("encrypted_aes256_ae1.zip"));

        var located = RawZipEntryLocator.Locate(fs, "compressible.txt");

        located.AeVersion.Should().Be(1);
        located.StoredCrc32.Should().NotBe(0); // AE-1 keeps the real CRC-32, unlike AE-2
    }

    [Fact]
    public void Locate_MixedArchive_FindsBothEncryptedAndPlainEntriesIndependently()
    {
        using var fs = File.OpenRead(FixtureHelper.Archive("mixed_encrypted_and_plain.zip"));

        var encrypted = RawZipEntryLocator.Locate(fs, "compressible.txt");
        var plain = RawZipEntryLocator.Locate(fs, "readme.txt");

        encrypted.GeneralPurposeEncryptedBit.Should().BeTrue();
        plain.GeneralPurposeEncryptedBit.Should().BeFalse();
    }

    [Fact]
    public void Locate_EntryNotInArchive_Throws()
    {
        using var fs = File.OpenRead(FixtureHelper.Archive("encrypted_aes256.zip"));

        var act = () => RawZipEntryLocator.Locate(fs, "does_not_exist.txt");

        act.Should().Throw<FileNotFoundException>();
    }
}
