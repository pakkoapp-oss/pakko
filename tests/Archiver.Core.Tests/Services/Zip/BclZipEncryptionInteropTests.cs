using System.IO.Compression;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services.Zip;

/// <summary>
/// T-F493: .NET 11's own ZIP encryption (<see cref="ZipEncryptionMethod"/>) against Pakko's, both
/// ways. Pakko keeps its reader and writer (docs/DECISIONS.md, "T-F493 measured"); these rows keep
/// what users get either way: an archive Pakko writes opens in any .NET 11 program, one a .NET 11
/// program writes opens in Pakko, and an archive of the released v1.10.0 opens in both. The last
/// two facts are the BCL's, pinned because the decision rests on them: a red one means the decision
/// is due again, not that Pakko broke.
/// </summary>
public sealed class BclZipEncryptionInteropTests : IDisposable
{
    private const string Password = "Corr3ct Horse~!";
    private const int AboveInMemoryLimit = 1024 * 1024 + 17;

    private readonly ZipArchiveService _sut = new(new GroupPolicyOptions());
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    // "привіт" built from code points, so no tool rewrites it on the way into this file.
    private static readonly string CyrillicName =
        new([(char)0x043F, (char)0x0440, (char)0x0438, (char)0x0432, (char)0x0456, (char)0x0442]);

    private static Func<PasswordPromptInfo, Task<PasswordDecision>> FixedPassword(string password) =>
        _ => Task.FromResult(new PasswordDecision { Password = password, ApplyToRemaining = true });

    private static byte[] RandomBytes(int length, int seed)
    {
        byte[] bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    private static Dictionary<string, byte[]> Contents() => new()
    {
        ["empty.txt"] = [],
        ["one.bin"] = [42],
        ["text.txt"] = System.Text.Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("compressible text ", 5_000))),
        [$"sub/{CyrillicName}.txt"] = System.Text.Encoding.UTF8.GetBytes(CyrillicName),
        ["sub/random.bin"] = RandomBytes(70_000, 1),
        ["large.bin"] = RandomBytes(AboveInMemoryLimit, 2),
    };

    private string WriteSourceTree(Dictionary<string, byte[]> contents)
    {
        string root = Path.Combine(_temp.Path, "src", "tree");
        foreach ((string name, byte[] bytes) in contents)
        {
            string path = Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }
        Directory.CreateDirectory(Path.Combine(root, "empty folder"));
        return root;
    }

    private static Dictionary<string, byte[]> ReadWithBcl(string zipPath, string password)
    {
        var read = new Dictionary<string, byte[]>();
        using ZipArchive zip = ZipFile.OpenRead(zipPath);
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            if (entry.FullName.EndsWith('/'))
            {
                entry.IsEncrypted.Should().BeFalse(entry.FullName);
                continue;
            }
            entry.IsEncrypted.Should().BeTrue(entry.FullName);
            using Stream stream = entry.Open(password);
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            read[entry.FullName] = copy.ToArray();
        }
        return read;
    }

    private async Task<string> ExtractWithPakkoAsync(string zipPath, string password)
    {
        string destination = Path.Combine(_temp.Path, "out-" + Path.GetFileNameWithoutExtension(zipPath));
        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [zipPath],
            DestinationFolder = destination,
            Mode = ExtractMode.SingleFolder,
            ResolvePasswordAsync = FixedPassword(password),
        });
        result.Success.Should().BeTrue(because: string.Join("; ", result.Errors.Select(e => e.Message)));
        return destination;
    }

    private static string FileUnder(string root, string relative) =>
        Directory.GetFiles(root, Path.GetFileName(relative), SearchOption.AllDirectories)
            .Should().ContainSingle(relative).Subject;

    // ── Pakko writes, the BCL reads ─────────────────────────────────────────

    [Theory]
    [InlineData(CompressionLevel.Optimal)]
    [InlineData(CompressionLevel.NoCompression)]
    [InlineData(CompressionLevel.Fastest)]
    public async Task PakkoWrittenArchive_BclReadsEveryEntryByteExact(CompressionLevel level)
    {
        Dictionary<string, byte[]> contents = Contents();
        string source = WriteSourceTree(contents);
        ArchiveResult result = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [source],
            DestinationFolder = Path.Combine(_temp.Path, "zip"),
            ArchiveName = "pakko",
            CompressionLevel = level,
            ResolvePasswordAsync = FixedPassword(Password),
        });
        result.Success.Should().BeTrue(because: string.Join("; ", result.Errors.Select(e => e.Message)));
        string zipPath = result.CreatedFiles.Should().ContainSingle().Subject;

        Dictionary<string, byte[]> read = ReadWithBcl(zipPath, Password);

        read.Should().HaveCount(contents.Count);
        foreach ((string name, byte[] bytes) in contents)
            read["tree/" + name].Should().Equal(bytes, name);
        using ZipArchive zip = ZipFile.OpenRead(zipPath);
        zip.Entries.Where(e => !e.FullName.EndsWith('/')).Select(e => e.EncryptionMethod)
            .Should().AllBeEquivalentTo(ZipEncryptionMethod.Aes256);
    }

    [Fact]
    public void ReleasedV1_10_0Archive_BclReadsItByteExact()
    {
        Dictionary<string, byte[]> read = ReadWithBcl(FixtureHelper.Archive("encrypted_aes256_pakko_v1_10_0.zip"), "testpassword");

        read.Should().HaveCount(2);
        read["compressible.txt"].Should().Equal(File.ReadAllBytes(Path.Combine(FixtureHelper.FilesDir, "compressible.txt")));
        read["empty.txt"].Should().BeEmpty();
    }

    [Fact]
    public async Task ReleasedV1_10_0Archive_PakkoExtractsItByteExact()
    {
        string destination = await ExtractWithPakkoAsync(FixtureHelper.Archive("encrypted_aes256_pakko_v1_10_0.zip"), "testpassword");

        File.ReadAllBytes(FileUnder(destination, "compressible.txt"))
            .Should().Equal(File.ReadAllBytes(Path.Combine(FixtureHelper.FilesDir, "compressible.txt")));
        new FileInfo(FileUnder(destination, "empty.txt")).Length.Should().Be(0);
    }

    [Theory]
    [InlineData("encrypted_aes256.zip")]
    [InlineData("encrypted_aes128.zip")]
    [InlineData("encrypted_aes256_ae1.zip")]
    [InlineData("encrypted_zipcrypto_real.zip")]
    public void SevenZipWrittenFixture_BclReadsWhatPakkoReads(string fixture)
    {
        Dictionary<string, byte[]> read = ReadWithBcl(FixtureHelper.Archive(fixture), "testpassword");

        read["compressible.txt"].Should().Equal(File.ReadAllBytes(Path.Combine(FixtureHelper.FilesDir, "compressible.txt")));
    }

    // ── The BCL writes, Pakko reads ─────────────────────────────────────────

    public static TheoryData<ZipEncryptionMethod, CompressionLevel> BclMethods()
    {
        var data = new TheoryData<ZipEncryptionMethod, CompressionLevel>();
        foreach (ZipEncryptionMethod method in new[] { ZipEncryptionMethod.Aes256, ZipEncryptionMethod.Aes192, ZipEncryptionMethod.Aes128, ZipEncryptionMethod.ZipCrypto })
            foreach (CompressionLevel level in new[] { CompressionLevel.Optimal, CompressionLevel.NoCompression })
                data.Add(method, level);
        return data;
    }

    private string WriteWithBcl(Dictionary<string, byte[]> contents, string password, ZipEncryptionMethod method, CompressionLevel level)
    {
        string zipPath = Path.Combine(_temp.Path, $"bcl-{method}-{level}.zip");
        using ZipArchive zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach ((string name, byte[] bytes) in contents)
        {
            ZipArchiveEntry entry = zip.CreateEntry(name, level, password, method);
            using Stream stream = entry.Open();
            stream.Write(bytes);
        }
        return zipPath;
    }

    [Theory]
    [MemberData(nameof(BclMethods))]
    public async Task BclWrittenArchive_PakkoExtractsEveryEntryByteExact(ZipEncryptionMethod method, CompressionLevel level)
    {
        Dictionary<string, byte[]> contents = Contents();
        string zipPath = WriteWithBcl(contents, Password, method, level);

        string destination = await ExtractWithPakkoAsync(zipPath, Password);

        foreach ((string name, byte[] bytes) in contents)
            File.ReadAllBytes(FileUnder(destination, name)).Should().Equal(bytes, name);
    }

    [Theory]
    [InlineData(ZipEncryptionMethod.Aes256)]
    [InlineData(ZipEncryptionMethod.ZipCrypto)]
    public async Task BclWrittenArchive_NonAsciiPassword_PakkoExtractsIt(ZipEncryptionMethod method)
    {
        // The BCL encodes a password as UTF-8; Pakko tries UTF-8 among its encodings.
        string password = "pw-" + CyrillicName;
        var contents = new Dictionary<string, byte[]> { ["text.txt"] = [1, 2, 3, 4, 5] };
        string zipPath = WriteWithBcl(contents, password, method, CompressionLevel.Optimal);

        string destination = await ExtractWithPakkoAsync(zipPath, password);

        File.ReadAllBytes(FileUnder(destination, "text.txt")).Should().Equal(contents["text.txt"]);
    }

    // ── The BCL's facts the decision rests on ───────────────────────────────

    [Fact]
    public async Task TamperedEntry_BclHandsOutPlaintextBeforeTheCode_PakkoWritesNothing()
    {
        var contents = new Dictionary<string, byte[]> { ["big.bin"] = RandomBytes(4 * 1024 * 1024, 3) };
        string zipPath = WriteWithBcl(contents, Password, ZipEncryptionMethod.Aes256, CompressionLevel.NoCompression);
        byte[] zipBytes = File.ReadAllBytes(zipPath);
        zipBytes[zipBytes.Length / 2] ^= 0x01;
        File.WriteAllBytes(zipPath, zipBytes);

        long handedOut = 0;
        Action bclRead = () =>
        {
            using ZipArchive zip = ZipFile.OpenRead(zipPath);
            using Stream stream = zip.Entries[0].Open(Password);
            byte[] buffer = new byte[81920];
            int n;
            while ((n = stream.Read(buffer)) > 0)
                handedOut += n;
        };
        bclRead.Should().Throw<InvalidDataException>();
        handedOut.Should().BeGreaterThan(0, "the BCL checks the authentication code only at the end of the stream (T-F493)");

        string destination = Path.Combine(_temp.Path, "out-tampered");
        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [zipPath],
            DestinationFolder = destination,
            Mode = ExtractMode.SingleFolder,
            ResolvePasswordAsync = FixedPassword(Password),
        });
        result.Success.Should().BeFalse();
        (Directory.Exists(destination) ? Directory.GetFiles(destination, "*", SearchOption.AllDirectories) : [])
            .Should().BeEmpty("a failed entry leaves nothing; that Pakko's reader hands out no byte of it is EncryptedZipEntryReaderTests' tampered row");
    }

    [Fact]
    public void WrongPassword_BclThrowsTheTypeItThrowsForCorruption()
    {
        string zipPath = WriteWithBcl(new() { ["a.txt"] = [1, 2, 3] }, Password, ZipEncryptionMethod.Aes256, CompressionLevel.Optimal);

        Action open = () =>
        {
            using ZipArchive zip = ZipFile.OpenRead(zipPath);
            using Stream stream = zip.Entries[0].Open("not the password");
        };

        open.Should().Throw<InvalidDataException>("a wrong password and a damaged entry share one exception type in the BCL");
    }
}
