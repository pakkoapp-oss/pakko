using System.IO.Compression;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

/// <summary>
/// End-to-end T-F189 coverage — the public API/pipeline wiring on top of T-F188's decryption
/// engine (see ZipArchiveServiceExtractTests/ZipArchiveServiceTestAsyncTests' own
/// PasswordProtectedZip characterization tests for the no-resolver-wired baseline this extends).
/// </summary>
public sealed class ZipArchiveServicePasswordTests : IDisposable
{
    private const string RealPassword = "testpassword";
    private readonly ZipArchiveService _sut = new();
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static Func<PasswordPromptInfo, Task<PasswordDecision>> FixedPassword(string password, bool applyToRemaining = false) =>
        _ => Task.FromResult(new PasswordDecision { Password = password, ApplyToRemaining = applyToRemaining });

    // ── Happy path ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("encrypted_aes256.zip")]
    [InlineData("encrypted_aes128.zip")]
    [InlineData("encrypted_zipcrypto_real.zip")]
    [InlineData("encrypted_aes256_ae1.zip")]
    public async Task ExtractAsync_EncryptedFixtureWithResolver_ExtractsByteExactContent(string fixtureName)
    {
        string expected = File.ReadAllText(Path.Combine(FixtureHelper.FilesDir, "compressible.txt"));
        var destDir = Path.Combine(_temp.Path, "out");

        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [FixtureHelper.Archive(fixtureName)],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
            ResolvePasswordAsync = FixedPassword(RealPassword),
        });

        result.Success.Should().BeTrue();
        string extracted = Directory.GetFiles(destDir, "compressible.txt", SearchOption.AllDirectories)
            .Should().ContainSingle().Subject;
        File.ReadAllText(extracted).Should().Be(expected);
    }

    [Fact]
    public async Task ExtractAsync_CyrillicEntryNameFixture_ExtractsUnderRealDecodedName()
    {
        var destDir = Path.Combine(_temp.Path, "out");

        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [FixtureHelper.Archive("encrypted_aes256_cyrillic_name.zip")],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
            ResolvePasswordAsync = FixedPassword(RealPassword),
        });

        result.Success.Should().BeTrue();
        string extracted = Directory.GetFiles(destDir, "unicode_filename_привіт.txt", SearchOption.AllDirectories)
            .Should().ContainSingle().Subject;
        File.Exists(extracted).Should().BeTrue();
    }

    [Fact]
    public async Task ExtractAsync_MixedArchive_ExtractsBothEntriesAndPromptsExactlyOnce()
    {
        int promptCount = 0;
        var destDir = Path.Combine(_temp.Path, "out");

        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [FixtureHelper.Archive("mixed_encrypted_and_plain.zip")],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
            ResolvePasswordAsync = info =>
            {
                promptCount++;
                return Task.FromResult(new PasswordDecision { Password = RealPassword });
            },
        });

        result.Success.Should().BeTrue();
        Directory.GetFiles(destDir, "compressible.txt", SearchOption.AllDirectories).Should().ContainSingle();
        Directory.GetFiles(destDir, "readme.txt", SearchOption.AllDirectories).Should().ContainSingle();
        promptCount.Should().Be(1);
    }

    [Fact]
    public async Task ExtractAsync_ApplyToRemaining_PromptsOnlyOnceAcrossTwoArchives()
    {
        // Both archives declare the same single entry name ("compressible.txt") — a single-file
        // archive under SeparateFolders mode lands directly in the shared DestinationFolder, not
        // a per-archive subfolder (T-F154's deliberate "no redundant wrapper" behavior), so the
        // second archive's entry collides with the first's unless a conflict resolution is wired.
        int promptCount = 0;
        string archiveA = Path.Combine(_temp.Path, "a.zip");
        string archiveB = Path.Combine(_temp.Path, "b.zip");
        File.Copy(FixtureHelper.Archive("encrypted_aes256.zip"), archiveA);
        File.Copy(FixtureHelper.Archive("encrypted_aes256.zip"), archiveB);
        var destDir = Path.Combine(_temp.Path, "out");

        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archiveA, archiveB],
            DestinationFolder = destDir,
            Mode = ExtractMode.SeparateFolders,
            OnConflict = ConflictBehavior.Rename,
            ResolvePasswordAsync = info =>
            {
                promptCount++;
                return Task.FromResult(new PasswordDecision { Password = RealPassword, ApplyToRemaining = true });
            },
        });

        result.Success.Should().BeTrue();
        promptCount.Should().Be(1);
        Directory.GetFiles(destDir, "compressible*.txt", SearchOption.AllDirectories).Should().HaveCount(2);
    }

    // ── Security & Boundary ──────────────────────────────────────────────────

    [Fact]
    public async Task ExtractAsync_WrongPasswordAllAttempts_RejectsWithUnchangedMessageAndLeavesNoPartialFiles()
    {
        int promptCount = 0;
        var destDir = Path.Combine(_temp.Path, "out");

        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [FixtureHelper.Archive("encrypted_aes256.zip")],
            DestinationFolder = destDir,
            ResolvePasswordAsync = _ =>
            {
                promptCount++;
                return Task.FromResult(new PasswordDecision { Password = "definitely-wrong" });
            },
        });

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Message == "This archive is password-protected and cannot be extracted.");
        promptCount.Should().Be(3); // PasswordResolver's maxAttempts for the Decrypt direction
        // DestinationFolder itself is always created upfront by ExtractAsync regardless of
        // per-archive outcome — the invariant that matters is that it stays EMPTY, and no "_tmp"
        // staging leftover survives a rejected archive.
        Directory.EnumerateFileSystemEntries(destDir).Should().BeEmpty();
        Directory.EnumerateFileSystemEntries(_temp.Path).Should().NotContain(p => p.EndsWith("_tmp"));
    }

    [Fact]
    public async Task ExtractAsync_UserCancelsPasswordPrompt_RejectsWithUnchangedMessage()
    {
        var destDir = Path.Combine(_temp.Path, "out");

        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [FixtureHelper.Archive("encrypted_aes256.zip")],
            DestinationFolder = destDir,
            ResolvePasswordAsync = FixedPassword(null!),
        });

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Message == "This archive is password-protected and cannot be extracted.");
        Directory.EnumerateFileSystemEntries(destDir).Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractAsync_TamperedAesFixtureCorrectPassword_HmacRejectsAndWritesNothing()
    {
        var destDir = Path.Combine(_temp.Path, "out");

        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [FixtureHelper.Archive("encrypted_aes256_tampered.zip")],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
            ResolvePasswordAsync = FixedPassword(RealPassword),
        });

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Message.Contains("authentication failed"));
        if (Directory.Exists(destDir))
            Directory.EnumerateFileSystemEntries(destDir, "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractAsync_EncryptedEntryWithTraversalName_StillRejectedBeforeDecryption()
    {
        // Hard invariant (T-F189 design): decryption plugs in only at entry.Open()'s replacement —
        // every other extraction safety check (here, the path-traversal guard) is provably
        // unmodified. Fixture is a REAL decryptable AES-256 entry (byte-patched name only), so a
        // rejection here can only come from the traversal check, not from failed password verification.
        var destDir = Path.Combine(_temp.Path, "out");

        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [FixtureHelper.Archive("encrypted_with_traversal_entry.zip")],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
            ResolvePasswordAsync = FixedPassword(RealPassword),
        });

        // Traversal escapes the whole temp destination -> InvalidDataException -> whole-archive
        // failure (same as any other traversal fixture in this suite), not a per-entry skip.
        result.Success.Should().BeFalse();
        File.Exists(Path.Combine(_temp.Path, "evil_trav.txt")).Should().BeFalse();
        Directory.EnumerateFileSystemEntries(_temp.Path).Should().NotContain(p => p.EndsWith("_tmp"));
    }

    [Fact]
    public async Task ExtractAsync_EncryptedEntryMotwPropagatesToDecryptedOutput()
    {
        string zipPath = Path.Combine(_temp.Path, "encrypted.zip");
        File.Copy(FixtureHelper.Archive("encrypted_aes256.zip"), zipPath);

        byte[] zoneBytes = System.Text.Encoding.ASCII.GetBytes("[ZoneTransfer]\r\nZoneId=3\r\n");
        try
        {
            using var adsStream = new FileStream(
                zipPath + ":Zone.Identifier", FileMode.Create, FileAccess.Write, FileShare.None);
            adsStream.Write(zoneBytes);
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException)
        {
            return; // ADS not supported on this volume (non-NTFS, network, etc.) — skip gracefully
        }

        var destDir = Path.Combine(_temp.Path, "out");
        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [zipPath],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
            ResolvePasswordAsync = FixedPassword(RealPassword),
        });

        result.Success.Should().BeTrue();
        string extracted = Directory.GetFiles(destDir, "compressible.txt", SearchOption.AllDirectories)
            .Should().ContainSingle().Subject;
        File.ReadAllBytes(extracted + ":Zone.Identifier").Should().Equal(zoneBytes);
    }

    [Fact]
    public async Task ExtractAsync_EncryptedEntry_ReportsRealByteAccurateFinalProgress()
    {
        // T-F189's streaming design point (docs/DECISIONS.md): the decrypted+decompressed stream
        // is wrapped by ProgressStream exactly like entry.Open() — proves it by checking the FINAL
        // report's byte total, not report count (a single ~40 KB entry against an 80 KB copy
        // buffer can legitimately produce just one report).
        var reports = new List<ProgressReport>();
        var progress = new Progress<ProgressReport>(reports.Add);
        var destDir = Path.Combine(_temp.Path, "out");

        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [FixtureHelper.Archive("encrypted_aes256.zip")],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
            ResolvePasswordAsync = FixedPassword(RealPassword),
        }, progress);

        // Progress<T> marshals via the SynchronizationContext/ThreadPool — give it a moment.
        await Task.Delay(50);

        result.Success.Should().BeTrue();
        reports.Should().NotBeEmpty();
        reports[^1].BytesTransferred.Should().Be(40803); // compressible.txt's real uncompressed size
    }

    // ── TestAsync ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("encrypted_aes256.zip")]        // AE-2 — HMAC alone, no header CRC to check
    [InlineData("encrypted_aes256_ae1.zip")]    // AE-1 — real header CRC, TrailerCrcCheckStream drain
    [InlineData("encrypted_zipcrypto_real.zip")] // ZipCrypto — TrailerCrcCheckStream drain
    public async Task TestAsync_EncryptedFixtureWithResolver_PassesCleanly(string fixtureName)
    {
        var result = await _sut.TestAsync(
            [FixtureHelper.Archive(fixtureName)],
            resolvePasswordAsync: FixedPassword(RealPassword));

        result.Success.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task TestAsync_TamperedAesFixtureCorrectPassword_FailsOnHmac()
    {
        var result = await _sut.TestAsync(
            [FixtureHelper.Archive("encrypted_aes256_tampered.zip")],
            resolvePasswordAsync: FixedPassword(RealPassword));

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Message.Contains("authentication failed"));
    }

    [Fact]
    public async Task TestAsync_WrongPassword_ReportsUnchangedRejectionMessage()
    {
        var result = await _sut.TestAsync(
            [FixtureHelper.Archive("encrypted_aes256.zip")],
            resolvePasswordAsync: FixedPassword("definitely-wrong"));

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Message == "This archive is password-protected and cannot be tested.");
    }

    [Fact]
    public async Task TestAsync_NoResolverWired_MatchesPreT189Message()
    {
        var result = await _sut.TestAsync([FixtureHelper.Archive("encrypted_aes256.zip")]);

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Message == "This archive is password-protected and cannot be tested.");
    }

    // ── Unsupported compression method under encryption (T-F194, advisor-caught) ──
    // encrypted_aes256_bzip2.zip: real 7za AES-256 over BZip2 (method 12). Before T-F194,
    // WrapDecompression threw NotSupportedException — inside ResolveArchivePasswordAsync's verify
    // callback, which none of its catch filters cover, so it escaped ExtractAsync/TestAsync
    // entirely, violating "Archiver.Core services never throw to callers."

    [Fact]
    public async Task ExtractAsync_Bzip2UnderAesCorrectPassword_ReportsUnsupportedMethodWithoutThrowing()
    {
        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [FixtureHelper.Archive("encrypted_aes256_bzip2.zip")],
            DestinationFolder = Path.Combine(_temp.Path, "out"),
            Mode = ExtractMode.SingleFolder,
            ResolvePasswordAsync = FixedPassword(RealPassword),
        });

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("unsupported compression method"));
    }

    [Fact]
    public async Task TestAsync_Bzip2UnderAesCorrectPassword_ReportsUnsupportedMethodWithoutThrowing()
    {
        var result = await _sut.TestAsync(
            [FixtureHelper.Archive("encrypted_aes256_bzip2.zip")],
            resolvePasswordAsync: FixedPassword(RealPassword));

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("unsupported compression method"));
    }

    [Theory]
    [InlineData((ushort)200)]
    [InlineData((ushort)2)]
    public async Task ExtractAsync_MalformedAesExtraRecord_ReportsErrorWithoutThrowing(ushort declaredSize)
    {
        string path = MalformedAesExtraFixture.Create(_temp.Path, declaredSize);

        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [path],
            DestinationFolder = Path.Combine(_temp.Path, "out"),
            Mode = ExtractMode.SingleFolder,
            ResolvePasswordAsync = FixedPassword(RealPassword),
        });

        result.Success.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData((ushort)200)]
    [InlineData((ushort)2)]
    public async Task ListEntriesAsync_MalformedAesExtraRecord_DoesNotThrow(ushort declaredSize)
    {
        string path = MalformedAesExtraFixture.Create(_temp.Path, declaredSize);

        Func<Task> act = () => _sut.ListEntriesAsync(path);

        await act.Should().NotThrowAsync();
    }

    // ── Characterization (regression, not failing-first — see docs/DECISIONS.md's T-F189 entry) ──

    [Fact]
    public async Task ExtractAsync_NoResolverWired_FullyEncryptedArchiveMessageIsByteIdenticalToPreT189()
    {
        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [FixtureHelper.Archive("encrypted_aes256.zip")],
            DestinationFolder = Path.Combine(_temp.Path, "out"),
        });

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Message == "This archive is password-protected and cannot be extracted.");
    }

    [Fact]
    public async Task ExtractAsync_NoResolverWired_MixedArchiveNowCorrectlyReportsPasswordProtectedNotCorrupted()
    {
        // T-F189 deliberately WIDENED IsEncryptedZip to scan the whole central directory, not just
        // the first entry — pre-T-F189, this exact fixture (plain entry first, encrypted entry
        // second) fell through to "File has ZIP signature but appears corrupted or incomplete."
        // That was the bug being fixed, not a byte-identical baseline to preserve — see
        // docs/DECISIONS.md's T-F189 entry.
        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [FixtureHelper.Archive("mixed_encrypted_and_plain.zip")],
            DestinationFolder = Path.Combine(_temp.Path, "out"),
        });

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Message == "This archive is password-protected and cannot be extracted.");
    }

    // ── ListEntriesAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task ListEntriesAsync_Ae2Entry_ReportsNullCrc32NotZero()
    {
        var result = await _sut.ListEntriesAsync(FixtureHelper.Archive("encrypted_aes256.zip"));

        result.Success.Should().BeTrue();
        result.Entries.Should().ContainSingle().Which.Crc32.Should().BeNull();
    }

    [Fact]
    public async Task ListEntriesAsync_ZipCryptoEntry_ReportsRealNonNullCrc32()
    {
        var result = await _sut.ListEntriesAsync(FixtureHelper.Archive("encrypted_zipcrypto_real.zip"));

        result.Success.Should().BeTrue();
        result.Entries.Should().ContainSingle().Which.Crc32.Should().NotBeNull().And.NotBe(0u);
    }

    [Fact]
    public async Task ListEntriesAsync_Ae1Entry_ReportsRealNonNullCrc32()
    {
        var result = await _sut.ListEntriesAsync(FixtureHelper.Archive("encrypted_aes256_ae1.zip"));

        result.Success.Should().BeTrue();
        result.Entries.Should().ContainSingle().Which.Crc32.Should().NotBeNull().And.NotBe(0u);
    }

    // ── T-F193 Phase 0: Zip64 layouts, end to end ─────────────────────────────

    [Fact]
    public async Task ExtractAsync_EncryptedArchiveWithFullZip64Directory_ExtractsByteExactContent()
    {
        string zip64 = Path.Combine(_temp.Path, "zip64.zip");
        Zip64DirectoryRewriter.Rewrite(FixtureHelper.Archive("encrypted_aes256.zip"), zip64);
        var destDir = Path.Combine(_temp.Path, "out");

        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [zip64],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
            ResolvePasswordAsync = FixedPassword(RealPassword),
        });

        result.Errors.Should().BeEmpty();
        File.ReadAllText(Directory.GetFiles(destDir, "compressible.txt", SearchOption.AllDirectories).Single())
            .Should().Be(File.ReadAllText(Path.Combine(FixtureHelper.FilesDir, "compressible.txt")));
    }

    [Fact]
    public async Task ExtractAsync_StdinArchiveWithZip64LocalHeader_ExtractsByteExactContent()
    {
        var destDir = Path.Combine(_temp.Path, "out");

        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [FixtureHelper.Archive("encrypted_aes256_stdin_zip64local.zip")],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
            ResolvePasswordAsync = FixedPassword(RealPassword),
        });

        result.Errors.Should().BeEmpty();
        File.ReadAllText(Directory.GetFiles(destDir, "compressible.txt", SearchOption.AllDirectories).Single())
            .Should().Be(File.ReadAllText(Path.Combine(FixtureHelper.FilesDir, "compressible.txt")));
    }
}
