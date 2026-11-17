using System.Formats.Tar;
using System.IO.Compression;
using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

// T-F545: a streamed tar list passes each row on as its "-tv" line arrives (T-F540), before T-F418's
// count check. tar.exe prints an owner's newline as it is (3.8.9 and 3.5.2, measured 2026-11-17), so
// one entry's "-tv" took two lines: every later name was shown with the line before its own - another
// entry's type, size and date - and only then did the counts fail the list. Batch 5j (T-F551): "-tv
// --numeric-owner" prints the owner and group as numbers (both builds), so no line is split at all.
[Collection("TarSandbox")]
public sealed class TarStreamedListingTests : IDisposable
{
    private readonly TarSandboxedService _sut = new(new GroupPolicyOptions());
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Integration]
    public async Task ListEntriesAsync_OwnerWithANewline_EveryRowItsOwn()
    {
        string archive = Path.Combine(_temp.Path, "owner.tar");
        using (FileStream file = File.Create(archive))
        using (var writer = new TarWriter(file, TarEntryFormat.Pax))
        {
            writer.WriteEntry(Entry("zero.txt", 5, owner: "root"));
            writer.WriteEntry(Entry("first.txt", 1, owner: "own\nx"));
            writer.WriteEntry(Entry("second.txt", 22, owner: "root"));
        }
        var rows = new List<ArchiveEntryInfo>();

        ArchiveListSummary summary = await _sut.ListEntriesAsync(archive, rows.Add);

        summary.Success.Should().BeTrue(summary.ErrorMessage);
        rows.Select(r => (r.Path, r.Size)).Should().Equal([("zero.txt", 5L), ("first.txt", 1L), ("second.txt", 22L)]);
    }

    // T-F551: what the 5h check still let through - a name that ends another ("z b.txt", "b.txt") and an
    // owner whose second half starts with a mode, before names tar.exe escapes.
    [Integration]
    public async Task ListEntriesAsync_CraftedOwnersAndSuffixNames_EveryRowItsOwn()
    {
        string archive = Path.Combine(_temp.Path, "crafted.tar");
        using (FileStream file = File.Create(archive))
        using (var writer = new TarWriter(file, TarEntryFormat.Pax))
        {
            writer.WriteEntry(Entry("z b.txt", 4, owner: "u " + "13  2020 b.txt" + "\n" + "x"));
            writer.WriteEntry(Entry("b.txt", 9, owner: "root"));
            writer.WriteEntry(Entry(Escaped, 2, owner: "x\n-rw-r--r-- 0 y"));
            writer.WriteEntry(Entry(Spelled, 6, owner: "root"));
        }
        var rows = new List<ArchiveEntryInfo>();

        ArchiveListSummary summary = await _sut.ListEntriesAsync(archive, rows.Add);

        summary.Success.Should().BeTrue(summary.ErrorMessage);
        rows.Select(r => (r.Path, r.Size)).Should().Equal([("z b.txt", 4L), ("b.txt", 9L), (Escaped, 2L), (Spelled, 6L)]);
    }

    // The names a "-tv" line ends with that are not the name alone: a link's target follows it.
    [Integration]
    public async Task ListEntriesAsync_LinksAndNamesLikeColumns_AllPassed()
    {
        string archive = Path.Combine(_temp.Path, "links.tar");
        using (FileStream file = File.Create(archive))
        using (var writer = new TarWriter(file, TarEntryFormat.Pax))
        {
            writer.WriteEntry(Entry("plain.txt", 3, owner: "a b c"));
            writer.WriteEntry(Entry("a -> b.txt", 1, owner: "root"));
            writer.WriteEntry(Entry("x link to y.txt", 1, owner: "root"));
            writer.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "sym") { LinkName = "plain.txt" });
            writer.WriteEntry(new PaxTarEntry(TarEntryType.HardLink, "hard") { LinkName = "plain.txt" });
            writer.WriteEntry(Entry(Escaped, 1, owner: "root"));
            writer.WriteEntry(Entry(Spelled, 1, owner: "root"));
        }
        var rows = new List<ArchiveEntryInfo>();

        ArchiveListSummary summary = await _sut.ListEntriesAsync(archive, rows.Add);

        summary.Success.Should().BeTrue(summary.ErrorMessage);
        rows.Select(r => r.Path).Should().Equal("plain.txt", "a -> b.txt", "x link to y.txt", "sym", "hard", Escaped, Spelled);
        rows[0].Size.Should().Be(3, "batch 5j: an owner with spaces moved the size column");
        rows[0].Modified.Should().NotBeNull();
    }

    // Batch 5j (P2): "-tv" split on spaces put an owner's or group's words into the size column,
    // so the whole-archive total T-F94's ratio check reads came out 0 and a bomb passed it.
    [Integration]
    public async Task ExtractAsync_BombWhoseOwnerAndGroupHaveSpaces_IsStillDeclined()
    {
        string archive = Path.Combine(_temp.Path, "bomb.tar.gz");
        using (FileStream file = File.Create(archive))
        using (var gzip = new GZipStream(file, CompressionLevel.SmallestSize))
        using (var writer = new TarWriter(gzip, TarEntryFormat.Pax))
        {
            writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "bomb.txt")
            {
                DataStream = new MemoryStream(new byte[20_000_000]),
                UserName = "a b c",
                GroupName = "d e f",
            });
        }
        string destination = Path.Combine(_temp.Path, "out");

        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archive],
            DestinationFolder = destination,
            Mode = ExtractMode.SingleFolder,
        });

        result.CreatedFiles.Should().BeEmpty();
        result.SkippedFiles.Should().Contain(skipped => skipped.Path == archive);
    }

    // Batch 5h's reviewer: an entry with an ACL prints its mode with a "+" after it ("-rw-r--r--+",
    // measured on 3.8.9 and 3.5.2); a name tar.exe escapes there is paired by that mode alone.
    [Integration]
    public async Task ListEntriesAsync_EscapedNamesWithAnAcl_AllPassed()
    {
        string archive = Path.Combine(_temp.Path, "acl.tar");
        using (FileStream file = File.Create(archive))
        using (var writer = new TarWriter(file, TarEntryFormat.Pax))
        {
            foreach (string name in new[] { "plain.txt", Escaped, Spelled })
            {
                var acl = new Dictionary<string, string> { ["SCHILY.acl.access"] = "user::rw-,user:1000:r--,group::r--,mask::r--,other::r--" };
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name, acl) { DataStream = new MemoryStream(new byte[1]), UserName = "u" });
            }
        }
        var rows = new List<ArchiveEntryInfo>();

        ArchiveListSummary summary = await _sut.ListEntriesAsync(archive, rows.Add);

        summary.Success.Should().BeTrue(summary.ErrorMessage);
        rows.Select(r => r.Path).Should().Equal("plain.txt", Escaped, Spelled);
    }

    // Names tar.exe prints escaped ("ctl\001x.txt", "back\\slash.txt"), which "-t" read back unescaped.
    private static readonly string Escaped = "ctl" + (char)1 + "x.txt";
    private static readonly string Spelled = "back" + (char)92 + "slash.txt";

    private static PaxTarEntry Entry(string name, int size, string owner) =>
        new(TarEntryType.RegularFile, name) { DataStream = new MemoryStream(new byte[size]), UserName = owner };
}
