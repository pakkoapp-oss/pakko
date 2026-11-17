using System.Formats.Tar;
using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

// T-F545: a streamed tar list passes each row on as its "-tv" line arrives (T-F540), before T-F418's
// count check. tar.exe prints an owner's newline as it is (3.8.9 and 3.5.2, measured 2026-11-17), so
// one entry's "-tv" took two lines: every later name was shown with the line before its own - another
// entry's type, size and date - and only then did the counts fail the list.
[Collection("TarSandbox")]
public sealed class TarStreamedListingTests : IDisposable
{
    private readonly TarSandboxedService _sut = new(new GroupPolicyOptions());
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Integration]
    public async Task ListEntriesAsync_OwnerWithANewline_PassesNoRowPairedWithAnotherLine()
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

        summary.Success.Should().BeFalse();
        rows.Select(r => (r.Path, r.Size)).Should().Equal([("zero.txt", 5L)], "a row is passed only on its own line's evidence");
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
