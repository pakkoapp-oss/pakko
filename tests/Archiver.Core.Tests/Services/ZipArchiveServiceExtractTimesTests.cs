using System.Text;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

/// <summary>
/// T-F298: extraction restores each entry's modification time, files and folder entries alike, by
/// 7-Zip's rule — the NTFS extra field (0x000A) first, then the Info-ZIP extended timestamp
/// (0x5455), else the DOS time. Archives come from <see cref="LegacyZipBuilder"/> so every time
/// field is set explicitly.
/// </summary>
public sealed class ZipArchiveServiceExtractTimesTests : IDisposable
{
    // 2019-03-04 10:20:30 local — an even second, exact in DOS time.
    private static readonly DateTime DosLocal = new(2019, 3, 4, 10, 20, 30, DateTimeKind.Local);
    private static readonly DateTime NtfsUtc = new DateTime(2020, 5, 6, 7, 8, 9, DateTimeKind.Utc).AddTicks(1234567);
    private static readonly DateTime UnixUtc = new(2018, 1, 2, 3, 4, 5, DateTimeKind.Utc);
    private static readonly DateTime FolderUtc = new(2017, 8, 9, 11, 12, 14, DateTimeKind.Utc);

    private readonly ZipArchiveService _sut = new(new GroupPolicyOptions());
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static LegacyZipBuilder.Entry FileEntry(string name, byte[]? extra = null, DateTime? dosLocal = null)
    {
        (ushort time, ushort date) = LegacyZipBuilder.DosDateTime(dosLocal ?? DosLocal);
        return new LegacyZipBuilder.Entry(Encoding.ASCII.GetBytes(name), Encoding.ASCII.GetBytes("content of " + name),
            Flags: 0, HostOs: LegacyZipBuilder.HostNtfs, CentralExtra: extra, DosTime: time, DosDate: date);
    }

    private static LegacyZipBuilder.Entry FolderEntry(string name, DateTime utc) =>
        new(Encoding.ASCII.GetBytes(name), [], HostOs: LegacyZipBuilder.HostNtfs,
            CentralExtra: LegacyZipBuilder.NtfsTimeExtra(utc.ToFileTimeUtc()));

    private string Zip(string name, params LegacyZipBuilder.Entry[] entries) =>
        LegacyZipBuilder.Write(Path.Combine(_temp.Path, name), entries);

    private async Task<string> ExtractAsync(string archive, ExtractMode mode = ExtractMode.SingleFolder, string? destDir = null)
    {
        destDir ??= Path.Combine(_temp.Path, "out-" + Path.GetRandomFileName());
        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archive],
            DestinationFolder = destDir,
            Mode = mode,
        });
        result.Success.Should().BeTrue(because: string.Join("; ", result.Errors.Select(e => e.Message)));
        return destDir;
    }

    [Fact]
    public async Task ExtractAsync_DosTimeOnly_FileGetsTheEntryTime()
    {
        string archive = Zip("dos.zip", FileEntry("a.txt"), FileEntry("b.txt"));

        string dest = await ExtractAsync(archive);

        File.GetLastWriteTime(Path.Combine(dest, "a.txt")).Should().Be(DosLocal);
    }

    [Fact]
    public async Task ExtractAsync_NtfsExtraField_FileGetsTheExactTime()
    {
        string archive = Zip("ntfs.zip",
            FileEntry("a.txt", LegacyZipBuilder.NtfsTimeExtra(NtfsUtc.ToFileTimeUtc())), FileEntry("b.txt"));

        string dest = await ExtractAsync(archive);

        File.GetLastWriteTimeUtc(Path.Combine(dest, "a.txt")).Should().Be(NtfsUtc);
    }

    [Fact]
    public async Task ExtractAsync_UnixExtendedTimestamp_FileGetsThatTime()
    {
        int unixSeconds = (int)new DateTimeOffset(UnixUtc).ToUnixTimeSeconds();
        string archive = Zip("unix.zip", FileEntry("a.txt", LegacyZipBuilder.UnixTimeExtra(unixSeconds)), FileEntry("b.txt"));

        string dest = await ExtractAsync(archive);

        File.GetLastWriteTimeUtc(Path.Combine(dest, "a.txt")).Should().Be(UnixUtc);
    }

    [Fact]
    public async Task ExtractAsync_NtfsAndUnixFieldsDisagree_NtfsWins()
    {
        int unixSeconds = (int)new DateTimeOffset(UnixUtc).ToUnixTimeSeconds();
        byte[] both = [.. LegacyZipBuilder.UnixTimeExtra(unixSeconds), .. LegacyZipBuilder.NtfsTimeExtra(NtfsUtc.ToFileTimeUtc())];
        string archive = Zip("both.zip", FileEntry("a.txt", both), FileEntry("b.txt"));

        string dest = await ExtractAsync(archive);

        File.GetLastWriteTimeUtc(Path.Combine(dest, "a.txt")).Should().Be(NtfsUtc);
    }

    public static TheoryData<string, byte[]> HostileTimeFields => new()
    {
        { "zero FILETIME", LegacyZipBuilder.NtfsTimeExtra(0) },
        { "FILETIME past DateTime.MaxValue", LegacyZipBuilder.NtfsTimeExtra(long.MaxValue) },
        { "negative FILETIME", LegacyZipBuilder.NtfsTimeExtra(-1) },
        { "tag size past the record", LegacyZipBuilder.NtfsTimeExtra(NtfsUtc.ToFileTimeUtc(), tagSize: 64) },
        { "record size past the extra block", [0x0A, 0x00, 0xFF, 0x00, 0, 0, 0, 0] },
        { "Unix flags without mtime", LegacyZipBuilder.UnixTimeExtra(1_000_000_000, flags: 0) },
    };

    [Theory]
    [MemberData(nameof(HostileTimeFields))]
    public async Task ExtractAsync_HostileTimeField_ExtractsWithTheDosTime(string _, byte[] extra)
    {
        string archive = Zip("hostile.zip", FileEntry("a.txt", extra), FileEntry("b.txt"));

        string dest = await ExtractAsync(archive);

        File.ReadAllText(Path.Combine(dest, "a.txt")).Should().Be("content of a.txt");
        File.GetLastWriteTime(Path.Combine(dest, "a.txt")).Should().Be(DosLocal);
    }

    [Fact]
    public async Task ExtractAsync_FolderEntry_NewDestination_FolderGetsItsTime()
    {
        string archive = Zip("folders.zip", FolderEntry("a/", FolderUtc), FileEntry("a/f.txt"), FileEntry("b.txt"));

        string dest = await ExtractAsync(archive);

        Directory.GetLastWriteTimeUtc(Path.Combine(dest, "a")).Should().Be(FolderUtc);
        File.GetLastWriteTime(Path.Combine(dest, "a", "f.txt")).Should().Be(DosLocal);
    }

    [Fact]
    public async Task ExtractAsync_FolderEntry_MergeIntoExistingDestination_NewFolderGetsItsTimeExistingOneIsKept()
    {
        string dest = Path.Combine(_temp.Path, "existing");
        Directory.CreateDirectory(Path.Combine(dest, "old"));
        DateTime oldFolderTime = new(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Directory.SetLastWriteTimeUtc(Path.Combine(dest, "old"), oldFolderTime);
        string archive = Zip("merge.zip",
            FolderEntry("a/", FolderUtc), FileEntry("a/f.txt"), FolderEntry("old/", FolderUtc), FileEntry("old/g.txt"));

        await ExtractAsync(archive, destDir: dest);

        Directory.GetLastWriteTimeUtc(Path.Combine(dest, "a")).Should().Be(FolderUtc);
        Directory.GetLastWriteTimeUtc(Path.Combine(dest, "old")).Should().NotBe(FolderUtc);
    }

    [Fact]
    public async Task ExtractAsync_SeparateFolders_SingleRootFolderEntry_TheCreatedFolderGetsTheRootTime()
    {
        // SeparateFolders + one root folder: actualDest stands in for "root/" (StripRootPrefix).
        string archive = Zip("rooted.zip", FolderEntry("root/", FolderUtc), FileEntry("root/f.txt"));

        string dest = await ExtractAsync(archive, ExtractMode.SeparateFolders);

        Directory.GetLastWriteTimeUtc(Path.Combine(dest, "rooted")).Should().Be(FolderUtc);
        File.GetLastWriteTime(Path.Combine(dest, "rooted", "f.txt")).Should().Be(DosLocal);
    }

    [Fact]
    public async Task ExtractAsync_ArchiveWithZoneIdentifier_FileKeepsMotwAndTheEntryTime()
    {
        string archive = Zip("motw.zip", FileEntry("a.txt", LegacyZipBuilder.NtfsTimeExtra(NtfsUtc.ToFileTimeUtc())), FileEntry("b.txt"));
        File.WriteAllText(archive + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");

        string dest = await ExtractAsync(archive);

        string file = Path.Combine(dest, "a.txt");
        File.ReadAllText(file + ":Zone.Identifier").Should().Contain("ZoneId=3");
        File.GetLastWriteTimeUtc(file).Should().Be(NtfsUtc);
    }

    // T-F358: the mark and the time are set before the file's one close; both copy paths (with a
    // progress reporter and without) must leave what T-F298 fixed.
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task ExtractAsync_WithOrWithoutProgressAndMark_FileHasTheEntryTimeAndItsContent(bool withProgress, bool marked)
    {
        string archive = Zip("both.zip", FileEntry("a.txt", LegacyZipBuilder.NtfsTimeExtra(NtfsUtc.ToFileTimeUtc())), FileEntry("b.txt"));
        if (marked)
            File.WriteAllText(archive + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");
        string dest = Path.Combine(_temp.Path, "out-both");

        ArchiveResult result = await _sut.ExtractAsync(
            new ExtractOptions { ArchivePaths = [archive], DestinationFolder = dest, Mode = ExtractMode.SingleFolder },
            withProgress ? new Progress<ProgressReport>() : null);

        result.Success.Should().BeTrue();
        string a = Path.Combine(dest, "a.txt"), b = Path.Combine(dest, "b.txt");
        File.GetLastWriteTimeUtc(a).Should().Be(NtfsUtc);
        File.GetLastWriteTime(b).Should().Be(DosLocal);
        File.ReadAllText(a).Should().Be("content of a.txt");
        File.Exists(a + ":Zone.Identifier").Should().Be(marked);
        File.Exists(b + ":Zone.Identifier").Should().Be(marked);
        if (marked)
            File.ReadAllText(b + ":Zone.Identifier").Should().Contain("ZoneId=3");
    }

    [Fact]
    public async Task ExtractAsync_EncryptedEntry_FileGetsTheEntryTime()
    {
        string source = Path.Combine(_temp.Path, "src");
        Directory.CreateDirectory(source);
        string file = Path.Combine(source, "secret.txt");
        File.WriteAllText(file, "secret");
        File.SetLastWriteTime(file, DosLocal);
        const string password = "s3cret-pass";
        ArchiveResult created = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [file],
            DestinationFolder = _temp.Path,
            ArchiveName = "enc",
            ResolvePasswordAsync = _ => Task.FromResult(new PasswordDecision { Password = password }),
        });
        created.Success.Should().BeTrue();

        string dest = Path.Combine(_temp.Path, "enc-out");
        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [created.CreatedFiles[0]],
            DestinationFolder = dest,
            Mode = ExtractMode.SingleFolder,
            ResolvePasswordAsync = _ => Task.FromResult(new PasswordDecision { Password = password }),
        });

        result.Success.Should().BeTrue(because: string.Join("; ", result.Errors.Select(e => e.Message)));
        File.GetLastWriteTime(Path.Combine(dest, "secret.txt")).Should().Be(DosLocal);
    }
}
