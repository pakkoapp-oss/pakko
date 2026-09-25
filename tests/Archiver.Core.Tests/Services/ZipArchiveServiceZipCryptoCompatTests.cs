using System.Text;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Services.Zip;
using Archiver.Core.Services.Zip.Decryption;
using Archiver.Core.Tests.Services.Antivirus;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F243 item 2: with a data descriptor (bit 3) the ZipCrypto check byte is the high byte of the
// file time (Info-ZIP; 7-Zip ZipHandler.cpp checks it the same way), not of the CRC-32 — the
// CRC is not known when the header is written. Checking the CRC byte rejected the right password.
public sealed class ZipArchiveServiceZipCryptoCompatTests : IDisposable
{
    private const string Password = "testpassword";
    private readonly ZipArchiveService _sut = new();
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static Func<PasswordPromptInfo, Task<PasswordDecision>> Fixed(string password) =>
        _ => Task.FromResult(new PasswordDecision { Password = password });

    private async Task<(ArchiveResult Result, string Dest)> ExtractAsync(string zip, string password)
    {
        string dest = Path.Combine(_temp.Path, "out-" + Guid.NewGuid().ToString("N"));
        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [zip],
            DestinationFolder = dest,
            Mode = ExtractMode.SingleFolder,
            ResolvePasswordAsync = Fixed(password),
        });
        return (result, dest);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExtractAsync_ZipCrypto_RightPassword_Extracts(bool dataDescriptor)
    {
        string zip = ZipCryptoFixture.Write(Path.Combine(_temp.Path, $"zc{dataDescriptor}.zip"), "a.txt",
            Encoding.ASCII.GetBytes("hello zipcrypto"), Encoding.ASCII.GetBytes(Password), dataDescriptor);

        var (result, dest) = await ExtractAsync(zip, Password);

        result.Success.Should().BeTrue(string.Join("; ", result.Errors.Select(e => e.Message)));
        File.ReadAllText(Path.Combine(dest, "a.txt")).Should().Be("hello zipcrypto");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TestAsync_ZipCrypto_RightPassword_Passes(bool dataDescriptor)
    {
        string zip = ZipCryptoFixture.Write(Path.Combine(_temp.Path, $"zct{dataDescriptor}.zip"), "a.txt",
            Encoding.ASCII.GetBytes("hello zipcrypto"), Encoding.ASCII.GetBytes(Password), dataDescriptor);

        var result = await _sut.TestAsync([zip], resolvePasswordAsync: Fixed(Password));

        result.Success.Should().BeTrue(string.Join("; ", result.Errors.Select(e => e.Message)));
    }

    // T-F243 item 3: "wrong103" passes encrypted_zipcrypto_store.zip's one-byte check (see
    // AntivirusScanServiceTests). It used to be accepted and the entry then failed as "corrupted";
    // now the password is rejected up front and the user is asked again.
    [Fact]
    public async Task ExtractAsync_CheckByteCollidingWrongPassword_IsRejectedAndAskedAgain()
    {
        var answers = new Queue<string>(["wrong103", Password]);
        int prompts = 0;
        string dest = Path.Combine(_temp.Path, "collide");

        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [FixtureHelper.Archive("encrypted_zipcrypto_store.zip")],
            DestinationFolder = dest,
            Mode = ExtractMode.SingleFolder,
            ResolvePasswordAsync = _ =>
            {
                prompts++;
                return Task.FromResult(new PasswordDecision { Password = answers.Dequeue() });
            },
        });

        prompts.Should().Be(2);
        result.Success.Should().BeTrue(string.Join("; ", result.Errors.Select(e => e.Message)));
        File.ReadAllText(Path.Combine(dest, "compressible.txt"))
            .Should().Be(File.ReadAllText(Path.Combine(FixtureHelper.FilesDir, "compressible.txt")));
    }

    [Fact]
    public async Task TestAsync_CheckByteCollidingWrongPasswordOnly_ReportsPasswordNotCorruption()
    {
        var result = await _sut.TestAsync([FixtureHelper.Archive("encrypted_zipcrypto_store.zip")],
            resolvePasswordAsync: Fixed("wrong103"));

        result.Success.Should().BeFalse();
        result.Errors.Should().NotContain(e => e.Message.Contains("CRC", StringComparison.OrdinalIgnoreCase));
        result.Errors.Should().ContainSingle().Which.Message.Should().Contain("password");
    }

    // Above ZipCryptoFullCheckLimitBytes only the one-byte check runs at password time, so a
    // colliding wrong password still gets through — the later CRC-32 must catch it everywhere.
    private (string Zip, string CollidingPassword) LargeZipCryptoWithCollidingPassword()
    {
        byte[] content = new byte[(int)ZipArchiveService.PasswordProbeFullCheckLimitBytes + 4096];
        new Random(3).NextBytes(content);
        string zip = ZipCryptoFixture.Write(Path.Combine(_temp.Path, "large.zip"), "big.bin", content,
            Encoding.ASCII.GetBytes(Password), dataDescriptor: false);

        using var fs = File.OpenRead(zip);
        var located = RawZipEntryLocator.LocateAll(fs).Single();
        string colliding = Enumerable.Range(0, 100_000).Select(i => "wrong" + i)
            .First(p => EncryptedZipEntryReader.VerifyPassword(fs, located, p) == EncryptedZipReadResult.Success);
        return (zip, colliding);
    }

    [Fact]
    public async Task ScanAsync_LargeEntry_CheckByteCollidingWrongPassword_IsNeverReportedClean()
    {
        var (zip, colliding) = LargeZipCryptoWithCollidingPassword();
        var scanner = new FakeAmsiScanner();
        var service = new AntivirusScanService(new TarCapabilities(), null, () => scanner, () => true);

        var result = await service.ScanAsync(new AntivirusScanOptions { ArchivePaths = [zip], ResolvePasswordAsync = Fixed(colliding) });

        result.Findings.Should().ContainSingle(f => f.EntryPath == "big.bin" && f.Verdict == ThreatVerdict.Inconclusive);
    }

    [Fact]
    public async Task ExtractAsync_LargeEntry_CheckByteCollidingWrongPassword_FailsTheEntry()
    {
        var (zip, colliding) = LargeZipCryptoWithCollidingPassword();

        var (result, dest) = await ExtractAsync(zip, colliding);

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Message.Should().Contain("big.bin");
        File.Exists(Path.Combine(dest, "big.bin")).Should().BeFalse();
    }

    // T-F244 item 3: a non-ASCII ZipCrypto password's bytes depend on the tool that made the
    // archive — Windows tools use the ANSI code page (7-Zip/NanaZip decode with CP_ACP), Linux ones
    // UTF-8. Pakko tried UTF-8 only, so a Cyrillic password from a Windows tool never matched.
    [Theory]
    [InlineData(1251)]
    [InlineData(65001)]
    public async Task ExtractAsync_CyrillicZipCryptoPassword_AnsiOrUtf8Bytes_Extracts(int passwordCodePage)
    {
        const string cyrillic = "пароль";
        byte[] passwordBytes = passwordCodePage == 65001
            ? Encoding.UTF8.GetBytes(cyrillic)
            : CodePagesEncodingProvider.Instance.GetEncoding(passwordCodePage)!.GetBytes(cyrillic);
        string zip = ZipCryptoFixture.Write(Path.Combine(_temp.Path, $"cyr{passwordCodePage}.zip"), "a.txt",
            Encoding.ASCII.GetBytes("secret"), passwordBytes, dataDescriptor: false);
        var sut = new ZipArchiveService { NameCodePages = ZipNameCodePages.FromCodePages(866, 1251) };
        string dest = Path.Combine(_temp.Path, $"cyr-out{passwordCodePage}");

        var result = await sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [zip],
            DestinationFolder = dest,
            Mode = ExtractMode.SingleFolder,
            ResolvePasswordAsync = Fixed(cyrillic),
        });

        result.Success.Should().BeTrue(string.Join("; ", result.Errors.Select(e => e.Message)));
        File.ReadAllText(Path.Combine(dest, "a.txt")).Should().Be("secret");
    }

    // Advisor-caught regression of the first T-F244 cut: the encoding was chosen per entry by the
    // one-byte check alone, so for ~1 archive in 256 the WRONG encoding (ANSI bytes of a password
    // really written as UTF-8) passed it, decrypted garbage and failed the right password. The
    // encoding is now chosen once per archive by a full check of a small probe entry.
    [Fact]
    public async Task ExtractAsync_Utf8Password_WhoseAnsiBytesAlsoPassTheCheckByte_Extracts()
    {
        const string cyrillic = "пароль";
        var ansi = CodePagesEncodingProvider.Instance.GetEncoding(1251)!;
        string zip = Path.Combine(_temp.Path, "ambiguous.zip");
        int seed = Enumerable.Range(0, 100_000).First(candidateSeed =>
        {
            ZipCryptoFixture.Write(zip, "a.txt", Encoding.ASCII.GetBytes("secret"), Encoding.UTF8.GetBytes(cyrillic),
                dataDescriptor: false, headerSeed: candidateSeed);
            using var fs = File.OpenRead(zip);
            var located = RawZipEntryLocator.LocateAll(fs).Single();
            using var region = new EntryRegionStream(fs, located.CompressedDataOffset, located.CompressedSize, ownsSource: false);
            bool passes = ZipCryptoStream.TryCreate(region, ansi.GetBytes(cyrillic), located.ZipCryptoCheckByte, out Stream? plaintext);
            plaintext?.Dispose();
            return passes;
        });
        ZipCryptoFixture.Write(zip, "a.txt", Encoding.ASCII.GetBytes("secret"), Encoding.UTF8.GetBytes(cyrillic),
            dataDescriptor: false, headerSeed: seed);
        var sut = new ZipArchiveService { NameCodePages = ZipNameCodePages.FromCodePages(866, 1251) };
        string dest = Path.Combine(_temp.Path, "ambiguous-out");

        var result = await sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [zip],
            DestinationFolder = dest,
            Mode = ExtractMode.SingleFolder,
            ResolvePasswordAsync = Fixed(cyrillic),
        });

        result.Success.Should().BeTrue(string.Join("; ", result.Errors.Select(e => e.Message)));
        File.ReadAllText(Path.Combine(dest, "a.txt")).Should().Be("secret");
    }

    [Fact]
    public async Task ExtractAsync_DescriptorEntry_WrongPassword_Fails()
    {
        string zip = ZipCryptoFixture.Write(Path.Combine(_temp.Path, "zcw.zip"), "a.txt",
            Encoding.ASCII.GetBytes("hello zipcrypto"), Encoding.ASCII.GetBytes(Password), dataDescriptor: true);

        var (result, dest) = await ExtractAsync(zip, "not-the-password");

        result.Success.Should().BeFalse();
        File.Exists(Path.Combine(dest, "a.txt")).Should().BeFalse();
    }
}
