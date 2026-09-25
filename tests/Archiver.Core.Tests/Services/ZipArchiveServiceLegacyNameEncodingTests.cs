using System.IO.Compression;
using System.Text;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Services.Antivirus;
using Archiver.Core.Services.Zip;
using Archiver.Core.Tests.Services.Antivirus;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F234: names without the UTF-8 flag were decoded as UTF-8 — garbled on screen and on disk, and
// two different names (cp866 0x80 "А", 0x81 "Б") collapsed into one file, silently. The service is
// pinned to the Russian/Ukrainian code pages here so the expectations hold on the en-US CI runner
// (OEM 437 / ANSI 1252) exactly as on a uk-UA machine (OEM 866 / ANSI 1251).
public sealed class ZipArchiveServiceLegacyNameEncodingTests : IDisposable
{
    private static readonly ZipNameCodePages Ru = ZipNameCodePages.FromCodePages(866, 1251);
    private static readonly ZipNameCodePages Us = ZipNameCodePages.FromCodePages(437, 1252);
    private const ushort Utf8Flag = 0x0800;

    private readonly ZipArchiveService _sut = new() { NameCodePages = Ru };
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static byte[] Cp866(string text) => CodePagesEncodingProvider.Instance.GetEncoding(866)!.GetBytes(text);

    private static LegacyZipBuilder.Entry Oem(string name, string content) =>
        new(Cp866(name), Encoding.ASCII.GetBytes(content));

    private string Legacy(string name, params LegacyZipBuilder.Entry[] entries) =>
        LegacyZipBuilder.Write(Path.Combine(_temp.Path, name), entries);

    private async Task<(ArchiveResult Result, string Dest)> ExtractAsync(
        string zip, ConflictBehavior conflict = ConflictBehavior.Overwrite)
    {
        string dest = Path.Combine(_temp.Path, "out-" + Path.GetFileNameWithoutExtension(zip));
        Directory.CreateDirectory(dest);
        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [zip],
            DestinationFolder = dest,
            Mode = ExtractMode.SingleFolder,
            OnConflict = conflict,
        });
        return (result, dest);
    }

    // --- Happy path ---

    [Fact]
    public async Task ListEntriesAsync_OemNamesWithoutFlag_ReturnsRealNames()
    {
        string zip = Legacy("cp866.zip", Oem("Тека/Документ_квартал.txt", "D"), Oem("А.txt", "A"));

        var result = await _sut.ListEntriesAsync(zip);

        result.Success.Should().BeTrue();
        result.Entries.Select(e => e.Path).Should().Equal("Тека/Документ_квартал.txt", "А.txt");
    }

    [Fact]
    public async Task ExtractAsync_OemNamesWithoutFlag_WritesRealNames()
    {
        string zip = Legacy("cp866.zip", Oem("Тека/Документ_квартал.txt", "D"), Oem("А.txt", "A"));

        var (result, dest) = await ExtractAsync(zip);

        result.Success.Should().BeTrue();
        File.ReadAllText(Path.Combine(dest, "Тека", "Документ_квартал.txt")).Should().Be("D");
        File.ReadAllText(Path.Combine(dest, "А.txt")).Should().Be("A");
    }

    [Fact]
    public async Task ExtractAsync_TwoOemNamesThatUsedToCollapse_BothExtractWithOwnContent()
    {
        // The T-F234 repro: 0x80 and 0x81 both became U+FFFD under UTF-8.
        string zip = Legacy("pair.zip", Oem("А.txt", "A"), Oem("Б.txt", "B"));

        var (result, dest) = await ExtractAsync(zip);

        result.Success.Should().BeTrue();
        File.ReadAllText(Path.Combine(dest, "А.txt")).Should().Be("A");
        File.ReadAllText(Path.Combine(dest, "Б.txt")).Should().Be("B");
    }

    [Fact]
    public async Task SevenZipFixture_UnicodePathExtraWins_EvenUnderUsCodePages()
    {
        // 7za's default: cp866 bytes, flag clear, plus 0x7075. Decoding cp866 as cp437 would give
        // "Ç.txt"; the extra must win.
        var sut = new ZipArchiveService { NameCodePages = Us };
        string zip = FixtureHelper.Archive("legacy_oem866_7za.zip");

        var result = await sut.ListEntriesAsync(zip);

        result.Entries.Select(e => e.Path).Should().BeEquivalentTo(
            ["А.txt", "Б.txt", "Тека", "Тека/Документ_квартал.txt"]);
    }

    [Fact]
    public async Task TestAsync_OemArchive_Passes()
    {
        string zip = Legacy("cp866.zip", Oem("А.txt", "A"), Oem("Б.txt", "B"));

        var result = await _sut.TestAsync([zip]);

        result.Success.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData(3)]
    [InlineData(80)]
    public async Task OwnArchives_RoundTripCyrillicAndEmojiNames_WhateverTheCodePages(int fileCount)
    {
        // 3 files = sequential ZipArchive writer, 80 = parallel ZipEntryWriter (threshold 64).
        string src = Path.Combine(_temp.Path, "src");
        Directory.CreateDirectory(src);
        for (int i = 0; i < fileCount; i++)
            File.WriteAllText(Path.Combine(src, $"файл_{i}_\U0001F600.txt"), i.ToString());
        var writer = new ZipArchiveService();
        await writer.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [src],
            DestinationFolder = _temp.Path,
            ArchiveName = "own",
            Mode = ArchiveMode.SingleArchive,
        });

        var result = await new ZipArchiveService { NameCodePages = Us }.ListEntriesAsync(Path.Combine(_temp.Path, "own.zip"));

        result.Entries.Where(e => !e.IsDirectory).Select(e => e.Path).Should().BeEquivalentTo(
            Enumerable.Range(0, fileCount).Select(i => $"src/файл_{i}_\U0001F600.txt"));
    }

    [Fact]
    public async Task OwnEncryptedArchive_RoundTripsCyrillicName()
    {
        string src = Path.Combine(_temp.Path, "звіт.txt");
        File.WriteAllText(src, "secret");
        var writer = new ZipArchiveService();
        await writer.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [src],
            DestinationFolder = _temp.Path,
            ArchiveName = "enc",
            Mode = ArchiveMode.SingleArchive,
            ResolvePasswordAsync = _ => Task.FromResult(new PasswordDecision { Password = "s3cret-pass" }),
        });

        var result = await new ZipArchiveService { NameCodePages = Us }.ListEntriesAsync(Path.Combine(_temp.Path, "enc.zip"));

        result.Entries.Select(e => e.Path).Should().Equal("звіт.txt");
    }

    [Fact]
    public async Task ScanAsync_OemNames_ScansAndReportsRealEntryNames()
    {
        string zip = Legacy("scan.zip", Oem("А.txt", "A"), Oem("Б.txt", "B"));
        var scanner = new FakeAmsiScanner();
        var service = new AntivirusScanService(new TarCapabilities(), null, () => scanner, () => true) { NameCodePages = Ru };

        var result = await service.ScanAsync(new AntivirusScanOptions { ArchivePaths = [zip], SelectedEntryPaths = ["Б.txt"] });

        result.Findings.Should().ContainSingle(f => f.EntryPath == "Б.txt" && f.Verdict == ThreatVerdict.Clean);
        scanner.ScannedContent["Б.txt"].Should().Equal("B"u8.ToArray());
    }

    // --- Security & Boundary ---

    [Fact]
    public async Task ExtractAsync_BackslashTraversalInLegacyName_IsRejectedAsUnsafe()
    {
        string zip = Legacy("trav.zip", Oem("ok.txt", "ok"), Oem(@"..\evil.txt", "evil"));

        var (result, dest) = await ExtractAsync(zip);

        result.Errors.Should().ContainSingle(e => e.Message.Contains("../evil.txt") && e.Message.Contains("unsafe"));
        File.Exists(Path.Combine(_temp.Path, "evil.txt")).Should().BeFalse();
        File.Exists(Path.Combine(dest, "ok.txt")).Should().BeTrue();
    }

    [Theory]
    [InlineData(ExtractMode.SingleFolder)]
    [InlineData(ExtractMode.SeparateFolders)]
    public async Task ExtractAsync_BackslashSeparators_ClassifyLikeSlashSeparators(ExtractMode mode)
    {
        // T-F243 item 1: "root\a.txt" + "root\b.txt" is one root folder, exactly like the '/' form.
        string backslash = Legacy("back.zip", Oem(@"root\a.txt", "a"), Oem(@"root\b.txt", "b"));
        string slash = Legacy("slash.zip", Oem("root/a.txt", "a"), Oem("root/b.txt", "b"));

        async Task<string[]> TreeAsync(string zip)
        {
            string dest = Path.Combine(_temp.Path, "tree-" + Path.GetFileNameWithoutExtension(zip) + mode);
            Directory.CreateDirectory(dest);
            var result = await _sut.ExtractAsync(new ExtractOptions
            {
                ArchivePaths = [zip],
                DestinationFolder = dest,
                Mode = mode,
            });
            result.Success.Should().BeTrue();
            return Directory.GetFiles(dest, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(dest, f).Replace("back", "X").Replace("slash", "X"))
                .Order(StringComparer.Ordinal)
                .ToArray();
        }

        (await TreeAsync(backslash)).Should().Equal(await TreeAsync(slash));
    }

    [Fact]
    public async Task ExtractAsync_NamesCollidingAfterDecoding_OneExtractsTheOtherIsAnError()
    {
        // Flagged UTF-8 with invalid bytes: 0x80 and 0x81 both decode to U+FFFD — distinct raw
        // names, one output path. Must never be a silent overwrite, whatever the conflict mode.
        string zip = Legacy("clash.zip",
            new LegacyZipBuilder.Entry([0x80, 0x2E, 0x74, 0x78, 0x74], "A"u8.ToArray(), Utf8Flag),
            new LegacyZipBuilder.Entry([0x81, 0x2E, 0x74, 0x78, 0x74], "B"u8.ToArray(), Utf8Flag),
            Oem("ok.txt", "ok"));

        var (result, dest) = await ExtractAsync(zip, ConflictBehavior.Overwrite);

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("same name as another entry"));
        File.ReadAllText(Path.Combine(dest, "�.txt")).Should().Be("A");
        File.Exists(Path.Combine(dest, "ok.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task TestAsync_NamesCollidingAfterDecoding_IsReported()
    {
        string zip = Legacy("clash.zip",
            new LegacyZipBuilder.Entry([0x80, 0x2E, 0x74, 0x78, 0x74], "A"u8.ToArray(), Utf8Flag),
            new LegacyZipBuilder.Entry([0x81, 0x2E, 0x74, 0x78, 0x74], "B"u8.ToArray(), Utf8Flag));

        var result = await _sut.TestAsync([zip]);

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("same name as another entry"));
    }

    [Fact]
    public async Task ExtractAsync_IdenticalRawDuplicates_KeepTheExistingConflictRules()
    {
        // T-F30: byte-identical duplicate names are an ordinary conflict, not a decoding collision.
        string zip = Legacy("dup.zip", Oem("А.txt", "first"), Oem("А.txt", "second"));

        var (result, dest) = await ExtractAsync(zip, ConflictBehavior.Skip);

        result.Errors.Should().BeEmpty();
        File.ReadAllText(Path.Combine(dest, "А.txt")).Should().Be("first");
    }

    [Fact]
    public async Task ExtractAsync_SelectedEntryPaths_UseDecodedNames()
    {
        string zip = Legacy("sel.zip", Oem("А.txt", "A"), Oem("Б.txt", "B"));
        string dest = Path.Combine(_temp.Path, "sel");
        Directory.CreateDirectory(dest);

        await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [zip],
            DestinationFolder = dest,
            Mode = ExtractMode.SingleFolder,
            SelectedEntryPaths = ["Б.txt"],
        });

        Directory.GetFiles(dest).Select(Path.GetFileName).Should().Equal("Б.txt");
    }

    // T-F243 item 5 (hypothesis "Test and Extract can disagree when local and central records
    // differ"): both take names from the central directory and data through the same readers, so
    // they agree; the local-header name is never used for a path, not even a traversal one.
    [Fact]
    public async Task LocalHeaderNameDiffersFromCentral_TestAndExtractAgree_CentralNameWins()
    {
        string zip = Legacy("mismatch.zip",
            new LegacyZipBuilder.Entry("a.txt"u8.ToArray(), "A"u8.ToArray(), LocalRawName: "../evil.txt"u8.ToArray()));

        var tested = await _sut.TestAsync([zip]);
        var (extracted, dest) = await ExtractAsync(zip);

        tested.Success.Should().BeTrue();
        extracted.Success.Should().BeTrue();
        File.ReadAllText(Path.Combine(dest, "a.txt")).Should().Be("A");
        File.Exists(Path.Combine(_temp.Path, "evil.txt")).Should().BeFalse();
    }

    // --- Misuse ---

    [Fact]
    public async Task ListEntriesAsync_UnixHostUtf8WithoutFlag_ReturnsRealNames()
    {
        string zip = Legacy("unix.zip",
            new LegacyZipBuilder.Entry(Encoding.UTF8.GetBytes("звіт.txt"), "x"u8.ToArray(), 0, LegacyZipBuilder.HostUnix));

        var result = await _sut.ListEntriesAsync(zip);

        result.Entries.Select(e => e.Path).Should().Equal("звіт.txt");
    }

    // --- Error path ---

    [Fact]
    public async Task ListEntriesAsync_MalformedExtra_StillListsWithHostRule()
    {
        string zip = Legacy("badextra.zip",
            new LegacyZipBuilder.Entry(Cp866("А.txt"), "A"u8.ToArray(), 0, LegacyZipBuilder.HostFat,
                [0x75, 0x70, 0xC8, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00]));

        var result = await _sut.ListEntriesAsync(zip);

        result.Success.Should().BeTrue();
        result.Entries.Select(e => e.Path).Should().Equal("А.txt");
    }

    [Fact]
    public void ExistingZipFixtures_NewNameReaderAgreesWithZipArchiveOnEntryCount()
    {
        // Fail-closed guard: a count mismatch makes an archive unreadable, so every committed
        // fixture .NET itself can open must also pass the new reader.
        foreach (string path in Directory.GetFiles(FixtureHelper.ArchivesDir, "*.zip"))
        {
            int expected;
            try
            {
                using var archive = ZipFile.OpenRead(path);
                expected = archive.Entries.Count;
            }
            catch (InvalidDataException)
            {
                continue;
            }

            using var reader = ZipArchiveReader.Open(path, Ru);
            reader.Entries.Should().HaveCount(expected, Path.GetFileName(path));
        }
    }
}
