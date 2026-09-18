using System.IO.Compression;
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

    // ── LocateAll (T-F189) ───────────────────────────────────────────────────

    [Fact]
    public void LocateAll_MixedArchive_CountAndOrderMatchZipArchiveEntries()
    {
        string path = FixtureHelper.Archive("mixed_encrypted_and_plain.zip");
        using var archive = ZipFile.OpenRead(path);
        using var fs = File.OpenRead(path);

        var located = RawZipEntryLocator.LocateAll(fs);

        located.Should().HaveCount(archive.Entries.Count);
        for (int i = 0; i < located.Count; i++)
        {
            // Positional pairing is the whole point (see LocateAll's own doc comment) — cross-check
            // by encrypted-bit-vs-name rather than re-decoding, since that's exactly what
            // ZipArchiveService itself will do with this list.
            bool nameLooksEncrypted = archive.Entries[i].FullName == "compressible.txt";
            located[i].GeneralPurposeEncryptedBit.Should().Be(nameLooksEncrypted);
        }
    }

    [Fact]
    public void LocateAll_PlainArchive_NoEntryReportsEncryptedBit()
    {
        using var fs = File.OpenRead(FixtureHelper.Archive("valid_multiple_files.zip"));

        var located = RawZipEntryLocator.LocateAll(fs);

        located.Should().NotBeEmpty();
        located.Should().OnlyContain(e => !e.GeneralPurposeEncryptedBit);
    }

    [Fact]
    public void LocateAll_CyrillicEntryName_PairsPositionallyWithoutNeedingToDecodeTheName()
    {
        string path = FixtureHelper.Archive("encrypted_aes256_cyrillic_name.zip");
        using var archive = ZipFile.OpenRead(path);
        using var fs = File.OpenRead(path);

        var located = RawZipEntryLocator.LocateAll(fs);

        located.Should().HaveCount(1);
        archive.Entries.Should().HaveCount(1);
        archive.Entries[0].FullName.Should().Be("unicode_filename_привіт.txt");
        located[0].GeneralPurposeEncryptedBit.Should().BeTrue();
        located[0].CompressionMethod.Should().Be(99); // WinZip AES
        located[0].AeVersion.Should().Be(2);
    }

    [Fact]
    public void LocateAll_EncryptedEntryWithTraversalName_StillParsesTheRecord()
    {
        // The locator's job is purely structural parsing — rejecting a malicious entry name is
        // ZipArchiveService's job (GetEntryNameRejectionReason / the traversal check), not this
        // class's. Confirms LocateAll doesn't itself choke on the hard-invariant fixture (two
        // entries — see this fixture's MANIFEST.sha256 comment for why it's sourced from the
        // mixed archive rather than a single-entry one).
        using var fs = File.OpenRead(FixtureHelper.Archive("encrypted_with_traversal_entry.zip"));

        var located = RawZipEntryLocator.LocateAll(fs);

        located.Should().HaveCount(2);
        located.Should().ContainSingle(e => e.GeneralPurposeEncryptedBit);
    }
}
