using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Services.Antivirus;
using Archiver.Core.Services.Zip;
using Archiver.Core.Tests.Helpers;
using Archiver.Core.Tests.Services.Antivirus;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

/// <summary>
/// T-F571: an entry whose local header and central directory disagree on encryption (bit 0, bit 6)
/// or the compression method is refused by every reader of its content, before any byte is decoded:
/// another program reads it by the local header, so the two would give different content. 7-Zip
/// (ZipIn.cpp, AreItemsEqual/FlagsAreSame) reports such an item as a Headers Error and does not
/// extract it. A disagreement on the name, CRC or size stays T-F280's warning.
/// </summary>
public sealed class ZipHeadersDisagreeTests : IDisposable
{
    private readonly ZipArchiveService _sut = new(new GroupPolicyOptions());
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private const uint LocalSignature = 0x04034b50;
    private const uint CentralSignature = 0x02014b50;
    private static readonly string[] Names = ["a.txt", "b.txt", "c.txt"];

    private string PlainZip(string name, CompressionLevel level = CompressionLevel.Optimal, int entries = 3)
    {
        string path = Path.Combine(_temp.Path, name);
        using ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (string entry in Names.Take(entries))
        {
            using var writer = new StreamWriter(zip.CreateEntry(entry, level).Open());
            writer.Write(string.Concat(Enumerable.Repeat(entry + " content ", 20)));
        }
        return path;
    }

    // The local (30-byte header, name at 30) or central (46-byte header, name at 46) header of an entry.
    private static int HeaderOffset(byte[] bytes, string entryName, bool central)
    {
        byte[] name = Encoding.ASCII.GetBytes(entryName);
        (uint signature, int fixedLength, int nameLengthAt) = central ? (CentralSignature, 46, 28) : (LocalSignature, 30, 26);
        for (int i = 0; i <= bytes.Length - fixedLength - name.Length; i++)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i)) == signature
                && BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(i + nameLengthAt)) == name.Length
                && bytes.AsSpan(i + fixedLength, name.Length).SequenceEqual(name))
                return i;
        }
        throw new InvalidOperationException($"No header for {entryName}");
    }

    private static void Patch(string path, string entryName, bool central, Action<byte[], int, int, int> change)
    {
        byte[] bytes = File.ReadAllBytes(path);
        int at = HeaderOffset(bytes, entryName, central);
        // flags, method
        change(bytes, at, at + (central ? 8 : 6), at + (central ? 10 : 8));
        File.WriteAllBytes(path, bytes);
    }

    // b.txt: the local header says ZipCrypto, the central directory says plain.
    private string LocalSaysEncrypted(string name)
    {
        string path = PlainZip(name);
        Patch(path, "b.txt", central: false, (bytes, _, flags, _) => bytes[flags] |= 0x01);
        return path;
    }

    // b.txt: the central directory says ZipCrypto, the local header says plain.
    private string CentralSaysEncrypted(string name)
    {
        string path = PlainZip(name);
        Patch(path, "b.txt", central: true, (bytes, _, flags, _) => bytes[flags] |= 0x01);
        return path;
    }

    // b.txt: deflated (8) by the central directory, stored (0) by the local header.
    private string MethodsDisagree(string name)
    {
        string path = PlainZip(name);
        Patch(path, "b.txt", central: false, (bytes, _, _, method) => bytes[method] = 0);
        return path;
    }

    private async Task<(ArchiveResult Result, string Destination)> ExtractAsync(
        string archive, IReadOnlyList<string>? selected = null, bool keepDamaged = false)
    {
        string destination = Path.Combine(_temp.Path, "x-" + Path.GetRandomFileName());
        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archive],
            DestinationFolder = destination,
            Mode = ExtractMode.SingleFolder,
            SelectedEntryPaths = selected,
            KeepDamagedFiles = keepDamaged,
        });
        return (result, destination);
    }

    private static void ShouldRefuseB(ArchiveResult result, string archive)
    {
        ArchiveError error = result.Errors.Should().ContainSingle().Subject;
        error.SourcePath.Should().Be(archive);
        error.Text!.Code.Should().Be(MessageCode.EntryHeadersDisagree);
        error.Text.Arguments.Select(a => a.ToString()).Should().Equal("b.txt");
    }

    public static TheoryData<string> Disagreements => ["local-encrypted", "central-encrypted", "methods"];

    private string Disagreeing(string kind, string name) => kind switch
    {
        "local-encrypted" => LocalSaysEncrypted(name),
        "central-encrypted" => CentralSaysEncrypted(name),
        _ => MethodsDisagree(name),
    };

    [Theory]
    [MemberData(nameof(Disagreements))]
    public async Task ExtractAsync_HeadersDisagree_RefusesThatEntryAndWritesNothingOfIt(string kind)
    {
        string archive = Disagreeing(kind, kind + ".zip");

        (ArchiveResult result, string destination) = await ExtractAsync(archive);

        ShouldRefuseB(result, archive);
        File.Exists(Path.Combine(destination, "b.txt")).Should().BeFalse();
        Directory.EnumerateFileSystemEntries(destination).Select(Path.GetFileName).Should().BeEquivalentTo(["a.txt", "c.txt"]);
        result.Warnings.Should().BeEmpty("the refusal is the error; T-F280's warning counts name, CRC and size only");
        // Central "encrypted" with a plain local header asks for no password: no entry would be decrypted.
    }

    [Theory]
    [MemberData(nameof(Disagreements))]
    public async Task ExtractAsync_HeadersDisagree_KeepDamagedKeepsNothingOfIt(string kind)
    {
        string archive = Disagreeing(kind, kind + "-kb.zip");

        (ArchiveResult result, string destination) = await ExtractAsync(archive, keepDamaged: true);

        ShouldRefuseB(result, archive);
        Directory.EnumerateFileSystemEntries(destination).Select(Path.GetFileName).Should().BeEquivalentTo(["a.txt", "c.txt"]);
    }

    [Fact]
    public async Task ExtractAsync_HeadersDisagreeOnAnEntryLeftOut_ReportsNothing()
    {
        (ArchiveResult result, string destination) = await ExtractAsync(LocalSaysEncrypted("subset.zip"), selected: ["c.txt"]);

        result.Errors.Should().BeEmpty();
        result.Success.Should().BeTrue();
        File.Exists(Path.Combine(destination, "c.txt")).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(Disagreements))]
    public async Task TestAsync_HeadersDisagree_ReportsThatEntry(string kind)
    {
        string archive = Disagreeing(kind, kind + "-t.zip");

        ArchiveResult result = await _sut.TestAsync([archive]);

        ShouldRefuseB(result, archive);
    }

    [Theory]
    [MemberData(nameof(Disagreements))]
    public async Task ScanAsync_HeadersDisagree_ThatEntryIsInconclusiveAndNotScanned(string kind)
    {
        string archive = Disagreeing(kind, kind + "-s.zip");
        var scanner = new FakeAmsiScanner();
        var service = new AntivirusScanService(new TarCapabilities(), new GroupPolicyOptions(), () => scanner,
            () => AmsiProviderState.Usable);

        ThreatScanResult result = await service.ScanAsync(new AntivirusScanOptions { ArchivePaths = [archive] });

        ThreatFinding finding = result.Findings.Should().ContainSingle().Subject;
        finding.EntryPath.Should().Be("b.txt");
        finding.Verdict.Should().Be(ThreatVerdict.Inconclusive);
        finding.ReasonText!.Code.Should().Be(MessageCode.EntryHeadersDisagree);
        scanner.Calls.Select(c => c.ContentName).Should().BeEquivalentTo(["a.txt", "c.txt"]);
    }

    [Fact]
    public void Entries_TheFlagFollowsItsEntryAcrossBatches()
    {
        string archive = PlainZip("batches.zip");
        Patch(archive, "c.txt", central: false, (bytes, _, flags, _) => bytes[flags] |= 0x40);

        using var catalog = ZipCatalog.Open(archive, ZipNameCodePages.Current(), ArchiveLimits.MaxEntries, batchEntries: 2, forceZip64Overlay: false);

        catalog.Entries().Select(e => (e.FullName, e.HeadersDisagree)).Should().Equal(("a.txt", false), ("b.txt", false), ("c.txt", true));
    }

    [Theory]
    [InlineData(CompressionLevel.Optimal)]
    [InlineData(CompressionLevel.NoCompression)]
    public async Task ExtractAsync_HeadersAgree_NothingIsRefused(CompressionLevel level)
    {
        (ArchiveResult result, string destination) = await ExtractAsync(PlainZip($"clean-{level}.zip", level));

        result.Errors.Should().BeEmpty();
        Directory.EnumerateFileSystemEntries(destination).Should().HaveCount(3);
    }
}
