using System.IO.Compression;
using System.Text;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F496: ExtractOptions.KeepDamagedFiles keeps what a damaged plain ZIP entry decoded, as
// "name.damaged", and nothing else changes: the entry is still an error, the other guards still run.
public sealed class ZipArchiveServiceKeepDamagedTests : IDisposable
{
    private const string Password = "s3cret-pass";
    private static readonly string LongContent = string.Concat(Enumerable.Repeat("integrity-check ", 64));

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Dest => Path.Combine(_temp.Path, "out");

    private string CreateZip(string name, CompressionLevel level, string docName = "doc.txt")
    {
        string zipPath = Path.Combine(_temp.Path, name);
        using ZipArchive archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        using (var ok = new StreamWriter(archive.CreateEntry("ok.txt", level).Open()))
            ok.Write("fine");
        using var doc = new StreamWriter(archive.CreateEntry(docName, level).Open());
        doc.Write(LongContent);
        return zipPath;
    }

    private string StoredZipWithFlippedByte()
    {
        string zip = CreateZip("stored.zip", CompressionLevel.NoCompression);
        FlipByteOf(zip, LongContent, 5);
        return zip;
    }

    private static void FlipByteOf(string zip, string content, int offset)
    {
        byte[] bytes = File.ReadAllBytes(zip);
        int dataAt = bytes.AsSpan().IndexOf(Encoding.ASCII.GetBytes(content[..32]));
        dataAt.Should().BeGreaterThan(0);
        bytes[dataAt + offset] ^= 0x01;
        File.WriteAllBytes(zip, bytes);
    }

    private static string Flipped(string content, int offset)
    {
        char[] chars = content.ToCharArray();
        chars[offset] = (char)(chars[offset] ^ 0x01);
        return new string(chars);
    }

    private Task<ArchiveResult> ExtractAsync(string zip, bool keep = true, GroupPolicyOptions? policy = null,
        ConflictBehavior onConflict = ConflictBehavior.Skip, string? password = null,
        IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Dest);
        return new ZipArchiveService(policy ?? new GroupPolicyOptions()).ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [zip],
            DestinationFolder = Dest,
            Mode = ExtractMode.SingleFolder,
            OnConflict = onConflict,
            KeepDamagedFiles = keep,
            ResolvePasswordAsync = password is null ? null : _ => Task.FromResult(new PasswordDecision { Password = password }),
        }, progress, cancellationToken);
    }

    private string Kept => Path.Combine(Dest, "doc.txt" + EntryDiskName.DamagedSuffix);

    private void AssertKept(ArchiveResult result, string expectedContent)
    {
        result.Success.Should().BeFalse();
        ArchiveError error = result.Errors.Should().ContainSingle().Subject;
        error.Text!.Code.Should().Be(MessageCode.EntryKeptDamaged);
        error.Message.Should().Contain("doc.txt").And.Contain("doc.txt.damaged");
        result.Sources.Should().ContainSingle().Which.Outcome.Should().Be(SourceOutcome.Partial, "an archive that lost data is never deletable");
        File.Exists(Path.Combine(Dest, "doc.txt")).Should().BeFalse("a damaged file never takes its good name");
        File.ReadAllText(Kept).Should().Be(expectedContent);
        File.ReadAllText(Path.Combine(Dest, "ok.txt")).Should().Be("fine");
        Directory.GetDirectories(Dest, ".pakko-*").Should().BeEmpty();
    }

    private void AssertNothingKept(string? root = null) =>
        Directory.EnumerateFiles(root ?? _temp.Path, "*" + EntryDiskName.DamagedSuffix + "*", SearchOption.AllDirectories)
            .Should().BeEmpty();

    [Fact]
    public async Task Off_StoredEntryWithFlippedByte_KeepsNothing()
    {
        ArchiveResult result = await ExtractAsync(StoredZipWithFlippedByte(), keep: false);

        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.CannotExtractEntry);
        AssertNothingKept();
        File.Exists(Path.Combine(Dest, "doc.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task StoredEntryWithFlippedByte_KeepsAllItsBytesAsDamaged()
    {
        ArchiveResult result = await ExtractAsync(StoredZipWithFlippedByte());

        AssertKept(result, Flipped(LongContent, 5));
    }

    // The CRC is checked on the read that reaches the declared size, and that read throws: its
    // bytes are the file's end and must be written too.
    [Fact]
    public async Task DeflatedEntryWithWrongCrc_KeepsTheWholeDecodedContent()
    {
        string zip = CreateZip("deflated.zip", CompressionLevel.Optimal);
        ZipArchiveServiceExtractIntegrityTests.PatchCrc(zip, "doc.txt", 0xDEADBEEF);

        ArchiveResult result = await ExtractAsync(zip);

        AssertKept(result, LongContent);
    }

    // Deflated: the decompressor stops at the declared size and the CRC of those bytes fails.
    [Fact]
    public async Task UnderstatedSizeDeflated_KeepsNoMoreThanTheDeclaredSize()
    {
        string zip = CreateZip("under.zip", CompressionLevel.Optimal);
        ZipArchiveServiceExtractIntegrityTests.PatchUncompressedSize(zip, "doc.txt", 10);

        ArchiveResult result = await ExtractAsync(zip);

        AssertKept(result, LongContent[..10]);
    }

    // Stored: .NET refuses the first read, past the declared size, and what its buffer then holds
    // is not known to be the entry's: nothing was decoded, so nothing is kept.
    [Fact]
    public async Task UnderstatedSizeStored_KeepsNothing()
    {
        string zip = CreateZip("under.zip", CompressionLevel.NoCompression);
        ZipArchiveServiceExtractIntegrityTests.PatchUncompressedSize(zip, "doc.txt", 10);

        ArchiveResult result = await ExtractAsync(zip);

        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.CannotExtractEntry);
        AssertNothingKept();
    }

    // Deflate does not resynchronise after a break: what is kept is what was decoded before it
    // (zeroed bytes inside a stored block decode as zeros), never more than the content.
    [Fact]
    public async Task DamagedDeflateStream_KeepsOnlyAPrefixOfTheContent()
    {
        byte[] content = new byte[300_000];
        new Random(382).NextBytes(content);
        string zip = Path.Combine(_temp.Path, "damaged.zip");
        using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using (var ok = new StreamWriter(archive.CreateEntry("ok.txt", CompressionLevel.Optimal).Open()))
                ok.Write("fine");
            using Stream doc = archive.CreateEntry("doc.txt", CompressionLevel.Optimal).Open();
            doc.Write(content);
        }
        byte[] bytes = File.ReadAllBytes(zip);
        (int local, _) = ZipArchiveServiceExtractIntegrityTests.FindHeaders(bytes, "doc.txt");
        int dataStart = local + 30 + "doc.txt".Length + BitConverter.ToUInt16(bytes, local + 28);
        Array.Clear(bytes, dataStart + 20_000, 70_000);
        File.WriteAllBytes(zip, bytes);

        ArchiveResult result = await ExtractAsync(zip);

        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.EntryKeptDamaged);
        byte[] kept = File.ReadAllBytes(Kept);
        kept.Length.Should().BeGreaterThan(20_000).And.BeLessThan(content.Length);
        kept.AsSpan(0, 19_000).SequenceEqual(content.AsSpan(0, 19_000)).Should().BeTrue("the bytes before the damage are the entry's own");
    }

    [Fact]
    public async Task ForbiddenByPolicy_KeepsNothing()
    {
        ArchiveResult result = await ExtractAsync(StoredZipWithFlippedByte(), policy: new GroupPolicyOptions { DisableKeepDamagedFiles = true });

        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.CannotExtractEntry);
        AssertNothingKept();
    }

    // Guard 5: a damaged AES entry cannot be told from a wrong password that passed the check value.
    [Fact]
    public async Task DamagedAesEntry_KeepsNothing()
    {
        string src = Path.Combine(_temp.Path, "src");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "doc.txt"), LongContent);
        ArchiveResult created = await new ZipArchiveService(new GroupPolicyOptions()).ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [Path.Combine(src, "doc.txt")],
            DestinationFolder = _temp.Path,
            ArchiveName = "enc",
            Mode = ArchiveMode.SingleArchive,
            ResolvePasswordAsync = _ => Task.FromResult(new PasswordDecision { Password = Password }),
        });
        created.Success.Should().BeTrue();
        string zip = Path.Combine(_temp.Path, "enc.zip");
        byte[] bytes = File.ReadAllBytes(zip);
        (int local, int central) = ZipArchiveServiceExtractIntegrityTests.FindHeaders(bytes, "doc.txt");
        bytes[(local + central) / 2] ^= 0x01;
        File.WriteAllBytes(zip, bytes);

        ArchiveResult result = await ExtractAsync(zip, password: Password);

        result.Success.Should().BeFalse();
        AssertNothingKept();
        File.Exists(Path.Combine(Dest, "doc.txt")).Should().BeFalse();
    }

    // Guard 3: the name checks run before anything is written, keep or not.
    [Fact]
    public async Task TraversalEntryWithDamage_IsStillRefused()
    {
        string zip = CreateZip("slip.zip", CompressionLevel.NoCompression, docName: "../evil.txt");
        FlipByteOf(zip, LongContent, 5);

        ArchiveResult result = await ExtractAsync(zip);

        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.UnsafeEntryPath);
        AssertNothingKept();
        File.Exists(Path.Combine(_temp.Path, "evil.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task KeptFile_CarriesTheArchivesDownloadMark()
    {
        string zip = StoredZipWithFlippedByte();
        File.WriteAllText(zip + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");

        await ExtractAsync(zip);

        File.Exists(Kept + ":Zone.Identifier").Should().BeTrue();
    }

    [Theory]
    [InlineData(ConflictBehavior.Overwrite)]
    [InlineData(ConflictBehavior.RenameExisting)]
    [InlineData(ConflictBehavior.Rename)]
    public async Task ExistingGoodFile_IsLeftAsItIs(ConflictBehavior onConflict)
    {
        Directory.CreateDirectory(Dest);
        File.WriteAllText(Path.Combine(Dest, "doc.txt"), "original");

        ArchiveResult result = await ExtractAsync(StoredZipWithFlippedByte(), onConflict: onConflict);

        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.EntryKeptDamaged);
        File.ReadAllText(Path.Combine(Dest, "doc.txt")).Should().Be("original");
        Directory.GetFiles(Dest).Select(Path.GetFileName).Should().BeEquivalentTo("doc.txt", "ok.txt", "doc.txt.damaged");
        File.ReadAllText(Kept).Should().Be(Flipped(LongContent, 5));
    }

    [Fact]
    public async Task ExistingDamagedFile_IsNeverReplaced()
    {
        Directory.CreateDirectory(Dest);
        File.WriteAllText(Kept, "kept before");

        ArchiveResult result = await ExtractAsync(StoredZipWithFlippedByte(), onConflict: ConflictBehavior.Overwrite);

        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.EntryKeptDamaged);
        File.ReadAllText(Kept).Should().Be("kept before");
        File.ReadAllText(Path.Combine(Dest, "doc.txt (1).damaged")).Should().Be(Flipped(LongContent, 5));
    }

    // Only damage keeps a file: a cancel in the middle of the entry leaves nothing behind.
    [Fact]
    public async Task CancelledInsideTheEntry_KeepsNothing()
    {
        byte[] content = new byte[4_000_000];
        new Random(496).NextBytes(content);
        string zip = Path.Combine(_temp.Path, "big.zip");
        using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using Stream doc = archive.CreateEntry("doc.bin", CompressionLevel.NoCompression).Open();
            doc.Write(content);
        }
        ZipArchiveServiceExtractIntegrityTests.PatchCrc(zip, "doc.bin", 0xDEADBEEF);
        using var cts = new CancellationTokenSource();
        var progress = new CancellingProgress(cts);

        Func<Task> act = () => ExtractAsync(zip, progress: progress, cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        AssertNothingKept();
    }

    private sealed class CancellingProgress(CancellationTokenSource cts) : IProgress<ProgressReport>
    {
        public void Report(ProgressReport value) => cts.Cancel();
    }
}
