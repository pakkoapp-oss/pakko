using System.IO.Compression;
using System.Text;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F570: one owner places every extracted name - files, folders, kept damaged files, every engine.
// T-F569's items were each one engine or one kind of item deciding a name without the others.
public sealed class ExtractionPlacementTests : IDisposable
{
    private static readonly string LongContent = string.Concat(Enumerable.Repeat("integrity-check ", 64));
    private static readonly TarCapabilities Gzip = new() { SupportsGzip = true, Version = "bsdtar 3.8.9" };

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Dest => Path.Combine(_temp.Path, "out");

    // A stored "doc" whose content has one flipped byte, so its CRC-32 fails; entries before and after it.
    private string ZipWithDamaged(string name, string doc, (string Name, string Content)[]? after = null)
    {
        string zip = Path.Combine(_temp.Path, name);
        using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(archive.CreateEntry(doc, CompressionLevel.NoCompression).Open()))
                writer.Write(LongContent);
            foreach ((string entry, string content) in after ?? [])
            {
                using var writer = new StreamWriter(archive.CreateEntry(entry, CompressionLevel.NoCompression).Open());
                writer.Write(content);
            }
        }
        byte[] bytes = File.ReadAllBytes(zip);
        int dataAt = bytes.AsSpan().IndexOf(Encoding.ASCII.GetBytes(LongContent[..32]));
        bytes[dataAt + 5] ^= 0x01;
        File.WriteAllBytes(zip, bytes);
        return zip;
    }

    private static string Flipped
    {
        get
        {
            char[] chars = LongContent.ToCharArray();
            chars[5] = (char)(chars[5] ^ 0x01);
            return new string(chars);
        }
    }

    private Task<ArchiveResult> ExtractAsync(IReadOnlyList<string> archives, ConflictBehavior onConflict,
        Func<ConflictInfo, Task<ConflictDecision>>? ask = null)
    {
        var policy = new GroupPolicyOptions();
        var router = new ExtractionRouter(new ZipArchiveService(policy), new BareGzipRecordingTar(), Gzip, policy);
        return router.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = archives,
            DestinationFolder = Dest,
            Mode = ExtractMode.SingleFolder,
            OnConflict = onConflict,
            ResolveConflictAsync = ask,
            KeepDamagedFiles = true,
        });
    }

    // T-F569 item 1: `x -kb -y data.zip data.txt.damaged.gz` - the gzip's file meets the kept one.
    [Theory]
    [InlineData(ConflictBehavior.Overwrite)]
    [InlineData(ConflictBehavior.Skip)]
    [InlineData(ConflictBehavior.Rename)]
    public async Task GzipNamedLikeAKeptFile_NeverReplacesIt(ConflictBehavior onConflict)
    {
        string zip = ZipWithDamaged("data.zip", "data.txt");
        string gz = Path.Combine(_temp.Path, "data.txt" + EntryDiskName.DamagedSuffix + ".gz");
        File.WriteAllBytes(gz, BareGzipTests.Member(Encoding.UTF8.GetBytes("from the gzip")));

        ArchiveResult result = await ExtractAsync([zip, gz], onConflict);

        File.ReadAllText(Path.Combine(Dest, "data.txt" + EntryDiskName.DamagedSuffix)).Should().Be(Flipped);
        result.Errors.Should().ContainSingle(e => e.Text!.Code == MessageCode.EntryKeptDamaged);
        Directory.EnumerateFiles(Dest).Should().Contain(f => File.ReadAllText(f) == "from the gzip",
            "a name Windows or a kept file took is renamed apart, whatever the conflict mode");
    }

    // T-F569 item 2: a good folder of the kept name, after the damaged entry, extracts.
    [Fact]
    public async Task LaterFolderNamedLikeAKeptFile_Extracts_AndTheKeptFileIsNumbered()
    {
        string zip = ZipWithDamaged("folder.zip", "doc.txt", [("doc.txt" + EntryDiskName.DamagedSuffix + "/x.bin", "inner")]);

        ArchiveResult result = await ExtractAsync([zip], ConflictBehavior.Skip);

        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.EntryKeptDamaged);
        File.ReadAllText(Path.Combine(Dest, "doc.txt" + EntryDiskName.DamagedSuffix, "x.bin")).Should().Be("inner");
        File.ReadAllText(Path.Combine(Dest, "doc.txt (1)" + EntryDiskName.DamagedSuffix)).Should().Be(Flipped);
        result.Errors[0].Message.Should().Contain("doc.txt (1)" + EntryDiskName.DamagedSuffix, "the message names the file as it is on disk");
    }

    // A later file of the kept name: the archive's own file takes its name, the kept one a number.
    [Fact]
    public async Task LaterFileNamedLikeAKeptFile_TakesItsName_AndTheKeptFileIsNumbered()
    {
        string zip = ZipWithDamaged("file.zip", "doc.txt", [("doc.txt" + EntryDiskName.DamagedSuffix, "the archive's own")]);

        ArchiveResult result = await ExtractAsync([zip], ConflictBehavior.Skip);

        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.EntryKeptDamaged);
        File.ReadAllText(Path.Combine(Dest, "doc.txt" + EntryDiskName.DamagedSuffix)).Should().Be("the archive's own");
        File.ReadAllText(Path.Combine(Dest, "doc.txt (1)" + EntryDiskName.DamagedSuffix)).Should().Be(Flipped);
    }

    // PR #281 left Ask with a kept file uncovered: the user's answer is about the good name, which
    // the damaged entry never takes.
    [Theory]
    [InlineData(ConflictResolution.Overwrite)]
    [InlineData(ConflictResolution.Rename)]
    [InlineData(ConflictResolution.Skip)]
    public async Task Ask_ForADamagedEntry_TheFileThereStays(ConflictResolution answer)
    {
        string zip = ZipWithDamaged("ask.zip", "doc.txt");
        Directory.CreateDirectory(Dest);
        File.WriteAllText(Path.Combine(Dest, "doc.txt"), "the user's");
        int asked = 0;

        ArchiveResult result = await ExtractAsync([zip], ConflictBehavior.Ask, _ =>
        {
            asked++;
            return Task.FromResult(new ConflictDecision { Resolution = answer });
        });

        asked.Should().Be(1);
        File.ReadAllText(Path.Combine(Dest, "doc.txt")).Should().Be("the user's");
        if (answer == ConflictResolution.Skip)
        {
            Directory.EnumerateFiles(Dest).Should().ContainSingle();
            result.Sources.Should().ContainSingle().Which.Outcome.Should().Be(SourceOutcome.NotProcessed, "the user skipped the only file");
            return;
        }
        File.ReadAllText(Path.Combine(Dest, "doc.txt" + EntryDiskName.DamagedSuffix)).Should().Be(Flipped);
        result.Sources.Should().ContainSingle().Which.Outcome.Should().Be(SourceOutcome.Partial);
    }

    // The kept file is placed before the archive's outcome is counted, in each archive's own folder too.
    [Fact]
    public async Task SeparateFolders_EachArchiveWithAKeptFile_IsPartial()
    {
        // Two files each, so each archive gets a folder of its own.
        string first = ZipWithDamaged("one.zip", "doc.txt", [("ok.txt", "fine")]);
        string second = ZipWithDamaged("two.zip", "doc.txt", [("ok.txt", "fine")]);
        var policy = new GroupPolicyOptions();
        var router = new ExtractionRouter(new ZipArchiveService(policy), new BareGzipRecordingTar(), Gzip, policy);

        ArchiveResult result = await router.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [first, second],
            DestinationFolder = Dest,
            Mode = ExtractMode.SeparateFolders,
            KeepDamagedFiles = true,
        });

        result.Sources.Select(s => s.Outcome).Should().Equal(SourceOutcome.Partial, SourceOutcome.Partial);
        File.ReadAllText(Path.Combine(Dest, "one", "doc.txt" + EntryDiskName.DamagedSuffix)).Should().Be(Flipped);
        File.ReadAllText(Path.Combine(Dest, "two", "doc.txt" + EntryDiskName.DamagedSuffix)).Should().Be(Flipped);
    }
}
