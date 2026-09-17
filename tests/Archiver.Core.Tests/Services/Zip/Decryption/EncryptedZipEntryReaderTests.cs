using Archiver.Core.Services.Zip.Decryption;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services.Zip.Decryption;

public sealed class EncryptedZipEntryReaderTests
{
    private const string RealPassword = "testpassword";
    private static readonly string CompressiblePlaintext = File.ReadAllText(
        Path.Combine(FixtureHelper.FilesDir, "compressible.txt"));

    // ── Happy path ───────────────────────────────────────────────────────────

    [Fact]
    public void TryOpen_ZipCryptoCorrectPassword_ReturnsByteExactContent()
    {
        var (result, content) = EncryptedZipEntryReader.TryOpen(
            FixtureHelper.Archive("encrypted_zipcrypto_real.zip"), "compressible.txt", RealPassword);

        result.Should().Be(EncryptedZipReadResult.Success);
        ReadAllText(content!).Should().Be(CompressiblePlaintext);
    }

    [Fact]
    public void TryOpen_Aes256CorrectPassword_ReturnsByteExactContent()
    {
        var (result, content) = EncryptedZipEntryReader.TryOpen(
            FixtureHelper.Archive("encrypted_aes256.zip"), "compressible.txt", RealPassword);

        result.Should().Be(EncryptedZipReadResult.Success);
        ReadAllText(content!).Should().Be(CompressiblePlaintext);
    }

    [Fact]
    public void TryOpen_Aes128CorrectPassword_ReturnsByteExactContent()
    {
        var (result, content) = EncryptedZipEntryReader.TryOpen(
            FixtureHelper.Archive("encrypted_aes128.zip"), "compressible.txt", RealPassword);

        result.Should().Be(EncryptedZipReadResult.Success);
        ReadAllText(content!).Should().Be(CompressiblePlaintext);
    }

    [Fact]
    public void TryOpen_SyntheticAe1CorrectPassword_VerifiesStoredCrcAndReturnsContent()
    {
        var (result, content) = EncryptedZipEntryReader.TryOpen(
            FixtureHelper.Archive("encrypted_aes256_ae1.zip"), "compressible.txt", RealPassword);

        result.Should().Be(EncryptedZipReadResult.Success);
        ReadAllText(content!).Should().Be(CompressiblePlaintext);
    }

    [Fact]
    public void TryOpen_MixedArchiveEncryptedEntry_DecryptsIndependentlyOfPlainEntry()
    {
        var (result, content) = EncryptedZipEntryReader.TryOpen(
            FixtureHelper.Archive("mixed_encrypted_and_plain.zip"), "compressible.txt", RealPassword);

        result.Should().Be(EncryptedZipReadResult.Success);
        ReadAllText(content!).Should().Be(CompressiblePlaintext);
    }

    // ── Security & Boundary ──────────────────────────────────────────────────

    [Theory]
    [InlineData("encrypted_zipcrypto_real.zip")]
    [InlineData("encrypted_aes256.zip")]
    [InlineData("encrypted_aes128.zip")]
    public void TryOpen_WrongPassword_ReturnsWrongPasswordWithNoContent(string fixture)
    {
        var (result, content) = EncryptedZipEntryReader.TryOpen(
            FixtureHelper.Archive(fixture), "compressible.txt", "not-the-real-password");

        result.Should().Be(EncryptedZipReadResult.WrongPassword);
        content.Should().BeNull();
    }

    [Fact]
    public void TryOpen_TamperedAesCiphertext_HmacRejectsEvenWithCorrectPassword()
    {
        var (result, content) = EncryptedZipEntryReader.TryOpen(
            FixtureHelper.Archive("encrypted_aes256_tampered.zip"), "compressible.txt", RealPassword);

        result.Should().Be(EncryptedZipReadResult.Corrupted);
        content.Should().BeNull();
    }

    [Fact]
    public void TryOpen_EmptyPassword_TreatedAsWrongPasswordNotAnException()
    {
        var act = () => EncryptedZipEntryReader.TryOpen(
            FixtureHelper.Archive("encrypted_aes256.zip"), "compressible.txt", string.Empty);

        act.Should().NotThrow();
        var (result, _) = EncryptedZipEntryReader.TryOpen(
            FixtureHelper.Archive("encrypted_aes256.zip"), "compressible.txt", string.Empty);
        result.Should().Be(EncryptedZipReadResult.WrongPassword);
    }

    // ── Misuse & Fool / Error path ───────────────────────────────────────────

    [Fact]
    public void TryOpen_PlainEntryInMixedArchive_ThrowsBecauseCallerShouldHaveCheckedFirst()
    {
        // EncryptedZipEntryReader is only ever meant to be invoked after IsEncryptedZip/the
        // per-entry general-purpose bit already gated the call — calling it on an unencrypted
        // entry is a programmer error, not a runtime condition to swallow silently.
        var act = () => EncryptedZipEntryReader.TryOpen(
            FixtureHelper.Archive("mixed_encrypted_and_plain.zip"), "readme.txt", RealPassword);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void TryOpen_EntryNameNotInArchive_PropagatesFileNotFound()
    {
        var act = () => EncryptedZipEntryReader.TryOpen(
            FixtureHelper.Archive("encrypted_aes256.zip"), "nope.txt", RealPassword);

        act.Should().Throw<FileNotFoundException>();
    }

    private static string ReadAllText(Stream s)
    {
        using var reader = new StreamReader(s);
        return reader.ReadToEnd();
    }
}
