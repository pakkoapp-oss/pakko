using System.IO.Compression;
using System.Security.Cryptography;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Services.Zip.Decryption;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

/// <summary>
/// T-F193 phase 2: WinZip AES-256 (AE-2) archive creation via <see cref="ArchiveOptions.ResolvePasswordAsync"/>.
/// Content is checked by round-tripping through Pakko's own reader; the entry headers are checked
/// through <see cref="RawZipEntryLocator"/>. Independent 7-Zip validation of the written bytes
/// lives in Archiver.Core.PerformanceTests' ZipEncryptionCompatibilityTests, since a writer/reader
/// pair that share a bug would still round-trip here.
/// </summary>
public sealed class ZipArchiveServiceEncryptTests : IDisposable
{
    private const string Password = "s3cret-pass";
    private const ushort WinZipAesMethod = 99;

    private readonly ZipArchiveService _sut = new();
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static Func<PasswordPromptInfo, Task<PasswordDecision>> FixedPassword(string? password) =>
        _ => Task.FromResult(new PasswordDecision { Password = password });

    private sealed class SynchronousProgress<T>(Action<T> onReport) : IProgress<T>
    {
        public void Report(T value) => onReport(value);
    }

    // src/ { a.txt, sub/b.txt, empty/ }
    private string CreateSourceTree(string name = "src")
    {
        string root = Path.Combine(_temp.Path, name);
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        Directory.CreateDirectory(Path.Combine(root, "empty"));
        File.WriteAllText(Path.Combine(root, "a.txt"), string.Concat(Enumerable.Repeat("alpha ", 200)));
        File.WriteAllText(Path.Combine(root, "sub", "b.txt"), "bravo");
        return root;
    }

    private async Task<ArchiveResult> ArchiveEncryptedAsync(
        IReadOnlyList<string> sources, string outDir, string? password = Password,
        ArchiveMode mode = ArchiveMode.SingleArchive, CompressionLevel level = CompressionLevel.Optimal,
        IProgress<ProgressReport>? progress = null)
    {
        return await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = sources,
            DestinationFolder = outDir,
            ArchiveName = mode == ArchiveMode.SingleArchive ? "out" : null,
            Mode = mode,
            CompressionLevel = level,
            ResolvePasswordAsync = FixedPassword(password),
        }, progress);
    }

    // A single-root-folder archive extracts without its root folder (SingleFolder mode's
    // no-double-nesting rule), so a "src/a.txt" entry lands at <returned>/a.txt.
    private async Task<string> ExtractAsync(string archivePath, string password)
    {
        string destDir = Path.Combine(_temp.Path, "x-" + Path.GetRandomFileName());
        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archivePath],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
            ResolvePasswordAsync = FixedPassword(password),
        });
        result.Success.Should().BeTrue(because: string.Join("; ", result.Errors.Select(e => e.Message)));
        return destDir;
    }

    private static List<(string Name, LocatedZipEntry Located)> ReadRawEntries(string archivePath)
    {
        using var zip = ZipFile.OpenRead(archivePath);
        using var fs = File.OpenRead(archivePath);
        var located = RawZipEntryLocator.LocateAll(fs);
        return zip.Entries.Select((e, i) => (e.FullName, located[i])).ToList();
    }

    private static void AssertFileEntriesAreAe2Aes256(string archivePath)
    {
        foreach (var (name, located) in ReadRawEntries(archivePath))
        {
            if (name.EndsWith('/'))
            {
                located.CompressionMethod.Should().Be(0, $"directory entry '{name}' carries no data");
                located.GeneralPurposeEncryptedBit.Should().BeFalse($"directory entry '{name}' is not encrypted");
                continue;
            }
            located.CompressionMethod.Should().Be(WinZipAesMethod, name);
            located.GeneralPurposeEncryptedBit.Should().BeTrue(name);
            located.AeVersion.Should().Be(2, name);
            located.AesStrengthBits.Should().Be(256, name);
            located.StoredCrc32.Should().Be(0u, $"AE-2 hides the CRC of '{name}'");
        }
    }

    private static List<string> EntryNames(string archivePath)
    {
        using var zip = ZipFile.OpenRead(archivePath);
        return zip.Entries.Select(e => e.FullName).ToList();
    }

    // ── Happy path ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ArchiveAsync_WithPassword_WritesAe2Aes256EntriesThatRoundTrip()
    {
        string src = CreateSourceTree();
        string outDir = Path.Combine(_temp.Path, "out");

        var result = await ArchiveEncryptedAsync([src], outDir);

        result.Success.Should().BeTrue(because: string.Join("; ", result.Errors.Select(e => e.Message)));
        string archive = result.CreatedFiles.Should().ContainSingle().Subject;
        AssertFileEntriesAreAe2Aes256(archive);

        string extracted = await ExtractAsync(archive, Password);
        File.ReadAllText(Path.Combine(extracted, "a.txt")).Should().Be(File.ReadAllText(Path.Combine(src, "a.txt")));
        File.ReadAllText(Path.Combine(extracted, "sub", "b.txt")).Should().Be("bravo");
        // Checked on the archive, not on disk: extraction currently drops empty folders for
        // every ZIP, encrypted or not (T-F197).
        EntryNames(archive).Should().Contain("src/empty/");
    }

    [Theory]
    [InlineData(ArchiveMode.SingleArchive)]
    [InlineData(ArchiveMode.SeparateArchives)]
    public async Task ArchiveAsync_WithPassword_EntryNamesMatchUnencryptedArchive(ArchiveMode mode)
    {
        string src = CreateSourceTree();
        string single = Path.Combine(_temp.Path, "file.txt");
        File.WriteAllText(single, "one file");

        var plain = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [src, single],
            DestinationFolder = Path.Combine(_temp.Path, "plain"),
            ArchiveName = mode == ArchiveMode.SingleArchive ? "out" : null,
            Mode = mode,
        });
        var encrypted = await ArchiveEncryptedAsync([src, single], Path.Combine(_temp.Path, "enc"), mode: mode);

        plain.Success.Should().BeTrue();
        encrypted.Success.Should().BeTrue(because: string.Join("; ", encrypted.Errors.Select(e => e.Message)));
        var plainFiles = plain.CreatedFiles.OrderBy(Path.GetFileName).ToList();
        var encFiles = encrypted.CreatedFiles.OrderBy(Path.GetFileName).ToList();
        encFiles.Select(Path.GetFileName).Should().Equal(plainFiles.Select(Path.GetFileName));
        for (int i = 0; i < plainFiles.Count; i++)
            EntryNames(encFiles[i]).Should().Equal(EntryNames(plainFiles[i]));
    }

    [Fact]
    public async Task ArchiveAsync_WithPasswordAndMoreThan64Files_RoundTrips()
    {
        string src = Path.Combine(_temp.Path, "many");
        Directory.CreateDirectory(src);
        for (int i = 0; i < 80; i++)
            File.WriteAllText(Path.Combine(src, $"f{i:D3}.txt"), $"content {i}");

        var result = await ArchiveEncryptedAsync([src], Path.Combine(_temp.Path, "out"));

        result.Success.Should().BeTrue();
        string archive = result.CreatedFiles.Single();
        AssertFileEntriesAreAe2Aes256(archive);
        string extracted = await ExtractAsync(archive, Password);
        for (int i = 0; i < 80; i++)
            File.ReadAllText(Path.Combine(extracted, $"f{i:D3}.txt")).Should().Be($"content {i}");
    }

    [Fact]
    public async Task ArchiveAsync_WithPasswordAndFileAboveInMemoryThreshold_RoundTrips()
    {
        byte[] payload = new byte[(int)(1.5 * 1024 * 1024)];
        RandomNumberGenerator.Fill(payload);
        string file = Path.Combine(_temp.Path, "big.bin");
        File.WriteAllBytes(file, payload);

        var result = await ArchiveEncryptedAsync([file], Path.Combine(_temp.Path, "out"));

        result.Success.Should().BeTrue();
        string archive = result.CreatedFiles.Single();
        AssertFileEntriesAreAe2Aes256(archive);
        string extracted = await ExtractAsync(archive, Password);
        File.ReadAllBytes(Path.Combine(extracted, "big.bin")).Should().Equal(payload);
    }

    [Fact]
    public async Task ArchiveAsync_WithPasswordAndEmptyFile_WritesStoredAesEntryOfSaltPvAndTagOnly()
    {
        string file = Path.Combine(_temp.Path, "empty.txt");
        File.WriteAllBytes(file, []);

        var result = await ArchiveEncryptedAsync([file], Path.Combine(_temp.Path, "out"));

        result.Success.Should().BeTrue();
        string archive = result.CreatedFiles.Single();
        var (_, located) = ReadRawEntries(archive).Single();
        located.CompressionMethod.Should().Be(WinZipAesMethod);
        located.RealCompressionMethod.Should().Be(0, "an empty input is Stored, never an empty Deflate stream");
        located.CompressedSize.Should().Be(16 + 2 + 10, "salt + password verifier + authentication code");
        string extracted = await ExtractAsync(archive, Password);
        File.ReadAllBytes(Path.Combine(extracted, "empty.txt")).Should().BeEmpty();
    }

    [Fact]
    public async Task ArchiveAsync_WithPasswordAndNoCompression_UsesStoredAsRealMethod()
    {
        string src = CreateSourceTree();

        var result = await ArchiveEncryptedAsync([src], Path.Combine(_temp.Path, "out"), level: CompressionLevel.NoCompression);

        result.Success.Should().BeTrue();
        string archive = result.CreatedFiles.Single();
        ReadRawEntries(archive).Where(e => !e.Name.EndsWith('/'))
            .Should().OnlyContain(e => e.Located.RealCompressionMethod == 0);
        string extracted = await ExtractAsync(archive, Password);
        File.ReadAllText(Path.Combine(extracted, "sub", "b.txt")).Should().Be("bravo");
    }

    [Fact]
    public async Task ArchiveAsync_WithEveryPrintableAsciiCharacterInPassword_RoundTrips()
    {
        string printable = new(Enumerable.Range(0x20, 0x80 - 0x20).Select(c => (char)c).ToArray());
        string src = CreateSourceTree();

        var result = await ArchiveEncryptedAsync([src], Path.Combine(_temp.Path, "out"), password: printable);

        result.Success.Should().BeTrue(because: string.Join("; ", result.Errors.Select(e => e.Message)));
        string extracted = await ExtractAsync(result.CreatedFiles.Single(), printable);
        File.ReadAllText(Path.Combine(extracted, "sub", "b.txt")).Should().Be("bravo");
    }

    [Fact]
    public async Task ArchiveAsync_PasswordOfExactly99Characters_IsAccepted()
    {
        string src = CreateSourceTree();

        var result = await ArchiveEncryptedAsync([src], Path.Combine(_temp.Path, "out"), password: new string('p', 99));

        result.Success.Should().BeTrue(because: string.Join("; ", result.Errors.Select(e => e.Message)));
    }

    [Fact]
    public async Task ArchiveAsync_WithPasswordInSeparateArchivesMode_EncryptsEveryArchive()
    {
        string src = CreateSourceTree();
        string single = Path.Combine(_temp.Path, "file.txt");
        File.WriteAllText(single, "one file");

        var result = await ArchiveEncryptedAsync([src, single], Path.Combine(_temp.Path, "out"), mode: ArchiveMode.SeparateArchives);

        result.Success.Should().BeTrue(because: string.Join("; ", result.Errors.Select(e => e.Message)));
        result.CreatedFiles.Should().HaveCount(2);
        foreach (string archive in result.CreatedFiles)
        {
            AssertFileEntriesAreAe2Aes256(archive);
            await ExtractAsync(archive, Password);
        }
    }

    [Fact]
    public async Task ArchiveAsync_WithPasswordInSeparateArchivesMode_PromptsOnceWithEncryptPurpose()
    {
        var prompts = new List<PasswordPromptInfo>();
        string a = _temp.CreateFile("a.txt", "a");
        string b = _temp.CreateFile("b.txt", "b");

        var result = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [a, b],
            DestinationFolder = Path.Combine(_temp.Path, "out"),
            Mode = ArchiveMode.SeparateArchives,
            ResolvePasswordAsync = info =>
            {
                prompts.Add(info);
                return Task.FromResult(new PasswordDecision { Password = Password });
            },
        });

        result.Success.Should().BeTrue();
        prompts.Should().ContainSingle().Which.Purpose.Should().Be(PasswordPurpose.Encrypt);
    }

    [Fact]
    public async Task ArchiveAsync_WithPasswordInSeparateArchivesMode_Reports100PercentOnlyOnceAtTheEnd()
    {
        var reports = new List<ProgressReport>();
        var files = Enumerable.Range(0, 4).Select(i => _temp.CreateFile($"f{i}.txt", new string('x', 4096))).ToList();

        var result = await ArchiveEncryptedAsync(files, Path.Combine(_temp.Path, "out"),
            mode: ArchiveMode.SeparateArchives,
            progress: new SynchronousProgress<ProgressReport>(r => { lock (reports) reports.Add(r); }));

        result.Success.Should().BeTrue();
        reports.Should().NotBeEmpty();
        reports[^1].Percent.Should().Be(100);
        reports.Count(r => r.Percent == 100).Should().Be(1,
            "a per-archive writer finishing must not report the whole operation as complete");
    }

    // ── Security & boundary ─────────────────────────────────────────────────

    [Fact]
    public async Task ArchiveAsync_WithPassword_IdenticalContentGetsDifferentSaltAndCiphertext()
    {
        string src = Path.Combine(_temp.Path, "twins");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "one.txt"), "identical payload");
        File.WriteAllText(Path.Combine(src, "two.txt"), "identical payload");

        var result = await ArchiveEncryptedAsync([src], Path.Combine(_temp.Path, "out"));

        string archive = result.CreatedFiles.Single();
        var located = ReadRawEntries(archive).Select(e => e.Located).ToList();
        byte[] bytes = File.ReadAllBytes(archive);
        byte[] first = bytes.AsSpan((int)located[0].CompressedDataOffset, (int)located[0].CompressedSize).ToArray();
        byte[] second = bytes.AsSpan((int)located[1].CompressedDataOffset, (int)located[1].CompressedSize).ToArray();
        first[..16].Should().NotEqual(second[..16], "every entry needs its own random salt");
        first.Should().NotEqual(second, "a reused key would reuse the CTR keystream across entries");
    }

    [Fact]
    public async Task ArchiveAsync_WithPassword_WrongPasswordCannotExtract()
    {
        string src = CreateSourceTree();
        var result = await ArchiveEncryptedAsync([src], Path.Combine(_temp.Path, "out"));
        string destDir = Path.Combine(_temp.Path, "x");

        var extract = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [result.CreatedFiles.Single()],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
            ResolvePasswordAsync = FixedPassword("not-the-password"),
        });

        extract.Success.Should().BeFalse();
        (Directory.Exists(destDir) ? Directory.GetFiles(destDir, "*", SearchOption.AllDirectories) : [])
            .Should().BeEmpty();
    }

    // ── Misuse & error path ─────────────────────────────────────────────────

    [Theory]
    [InlineData(ArchiveMode.SingleArchive)]
    [InlineData(ArchiveMode.SeparateArchives)]
    public async Task ArchiveAsync_PasswordPromptCancelled_CreatesNothingAndFails(ArchiveMode mode)
    {
        string src = CreateSourceTree();
        string outDir = Path.Combine(_temp.Path, "out");

        var result = await ArchiveEncryptedAsync([src], outDir, password: null, mode: mode);

        result.Success.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
        result.CreatedFiles.Should().BeEmpty();
        (Directory.Exists(outDir) ? Directory.GetFileSystemEntries(outDir) : []).Should().BeEmpty();
    }

    // User decision 2026-09-24: 7-Zip's own creation rule (printable ASCII only, at most 99
    // characters for AES). 7-Zip decodes a ZIP password through the ANSI code page, so a Cyrillic
    // password would silently produce an archive 7-Zip/NanaZip call "Wrong password".
    [Theory]
    [InlineData("", "empty")]
    [InlineData("пароль", "ASCII")]
    [InlineData("pass\u00e9", "ASCII")]
    [InlineData("tab\there", "ASCII")]
    [InlineData("line\nbreak", "ASCII")]
    [InlineData("100", "99 characters")]
    public async Task ArchiveAsync_PasswordOther7ZipCannotUse_IsRejectedAndCreatesNothing(string password, string expectedReason)
    {
        if (password == "100") password = new string('p', 100);
        string src = CreateSourceTree();
        string outDir = Path.Combine(_temp.Path, "out");

        var result = await ArchiveEncryptedAsync([src], outDir, password: password);

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Message.Should().Contain(expectedReason);
        result.CreatedFiles.Should().BeEmpty();
        (Directory.Exists(outDir) ? Directory.GetFileSystemEntries(outDir) : []).Should().BeEmpty();
    }

    [Fact]
    public async Task ArchiveAsync_PasswordPromptCancelled_LeavesExistingDestinationUntouchedAndNeverAsksAboutTheConflict()
    {
        string src = CreateSourceTree();
        string outDir = Path.Combine(_temp.Path, "out");
        Directory.CreateDirectory(outDir);
        string existing = Path.Combine(outDir, "out.zip");
        File.WriteAllText(existing, "the user's previous archive");
        int conflictPrompts = 0;

        var result = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [src],
            DestinationFolder = outDir,
            ArchiveName = "out",
            OnConflict = ConflictBehavior.Ask,
            ResolveConflictAsync = _ =>
            {
                conflictPrompts++;
                return Task.FromResult(new ConflictDecision { Resolution = ConflictResolution.Overwrite });
            },
            ResolvePasswordAsync = FixedPassword(null),
        });

        result.Success.Should().BeFalse();
        conflictPrompts.Should().Be(0);
        File.ReadAllText(existing).Should().Be("the user's previous archive");
    }
}
