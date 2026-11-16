using Archiver.Core.Models;
using FluentAssertions;
using Xunit.Abstractions;

namespace Archiver.App.Core.Tests;

[CollectionDefinition("ArchiveTreeMemory", DisableParallelization = true)]
public sealed class ArchiveTreeMemoryGroup;

// T-F535: what the browser's index of an archive holds per entry once its listing is gone, as live
// bytes after a full collection. Alone in its collection: another test allocating meanwhile would
// count here. Before: a node, a dictionary per folder and the listing's own entries.
[Collection("ArchiveTreeMemory")]
public sealed class ArchiveTreeMemoryTests(ITestOutputHelper output)
{
    private const int Entries = 200_000;

    [Fact]
    public void Index_HoldsAtMostItsBudgetPerEntry()
    {
        long before = GC.GetTotalMemory(forceFullCollection: true);
        ArchiveTree tree = BuildOfAListingThatIsGone();
        long after = GC.GetTotalMemory(forceFullCollection: true);
        tree.TryGetChildren("d00000", out IReadOnlyList<ArchiveEntryViewModel> rows).Should().BeTrue();
        rows.Should().HaveCount(1000);
        GC.KeepAlive(tree);

        long perEntry = (after - before) / Entries;
        output.WriteLine($"{Entries} entries: {(after - before) >> 20} MiB live, {perEntry} B an entry");
        perEntry.Should().BeLessThanOrEqualTo(140, "measured 116 B: the path (about 64 B), its 48-byte row and its sorted position; 318 with a node per folder and the listing kept");
    }

    private static ArchiveTree BuildOfAListingThatIsGone()
    {
        var entries = new List<ArchiveEntryInfo>(Entries);
        for (int i = 0; i < Entries; i++)
        {
            entries.Add(new ArchiveEntryInfo
            {
                Path = $"d{i / 1000:D5}/f{i:D8}.txt",
                Size = i,
                CompressedSize = i,
                Crc32 = (uint)i,
                Modified = new DateTime(2020, 1, 1),
                Encryption = EntryEncryption.None,
            });
        }
        return ArchiveTreeIndex.Build(entries);
    }
}
