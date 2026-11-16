using Archiver.Core.Models;
using FluentAssertions;

namespace Archiver.App.Core.Tests;

// T-F535: the compact index against the tree it replaced (LegacyArchiveTree): every folder's rows,
// on random path sets with a fixed seed and on the shapes the sorted order makes hard.
public sealed class ArchiveTreeOracleTests
{
    public static TheoryData<string[]> Shapes =>
    [
        ["a/b c", "a/b/x", "a/b"],
        ["a/b/x", "a/b", "a/b c"],
        ["a/x.txt", "a/x.txt", "b"],
        ["a", "a/b"],
        ["a/b", "a"],
        ["/a/b", "a/c", "/a"],
        ["/", "//x", "a//b"],
        ["a!/x", "a/x", "a", "a-"],
        [],
    ];

    [Theory]
    [MemberData(nameof(Shapes))]
    public void Shapes_ListAsTheOldTree(string[] paths)
    {
        List<ArchiveEntryInfo> entries = [.. paths.Select((p, i) => Entry(p, isDirectory: i % 2 == 1, i))];

        AssertSame(entries);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void RandomSets_ListAsTheOldTree(int seed)
    {
        var random = new Random(seed);
        char[] alphabet = ['a', 'b', ' ', '/', '.', '!'];
        var entries = new List<ArchiveEntryInfo>();
        for (int i = 0; i < 400; i++)
        {
            int length = random.Next(1, 9);
            string path = new([.. Enumerable.Range(0, length).Select(_ => alphabet[random.Next(alphabet.Length)])]);
            path = random.Next(6) == 0 ? "/" + path : path;
            entries.Add(Entry(path.TrimEnd('/') is { Length: > 0 } trimmed ? trimmed : path, random.Next(3) == 0, i));
            if (random.Next(10) == 0)
                entries.Add(Entry(entries[random.Next(entries.Count)].Path, random.Next(2) == 0, i + 1000));
        }

        AssertSame(entries);
    }

    private static void AssertSame(List<ArchiveEntryInfo> entries)
    {
        var oracle = new LegacyArchiveTree(entries);
        ArchiveTree tree = ArchiveTreeIndex.Build(entries);
        foreach (string folder in FoldersToAsk(entries))
        {
            bool expectedFound = oracle.TryGetChildren(folder, out IReadOnlyList<ArchiveEntryViewModel> expected);
            bool found = tree.TryGetChildren(folder, out IReadOnlyList<ArchiveEntryViewModel> actual);

            found.Should().Be(expectedFound, "folder \"{0}\"", folder);
            actual.Should().Equal(expected, "folder \"{0}\"", folder);
        }
    }

    // The root, every prefix of every path at a '/', every path itself, and each with a leading '/'.
    private static HashSet<string> FoldersToAsk(List<ArchiveEntryInfo> entries)
    {
        var folders = new HashSet<string>(StringComparer.Ordinal) { string.Empty, "/", "nope", "a/nope" };
        foreach (string path in entries.Select(e => e.Path))
        {
            folders.Add(path);
            folders.Add("/" + path);
            for (int slash = path.IndexOf('/'); slash >= 0; slash = path.IndexOf('/', slash + 1))
            {
                folders.Add(path[..slash]);
                folders.Add("/" + path[..slash]);
            }
        }
        return folders;
    }

    private static ArchiveEntryInfo Entry(string path, bool isDirectory, int seed) => new()
    {
        Path = path,
        IsDirectory = isDirectory,
        Size = isDirectory ? 0 : seed,
        CompressedSize = isDirectory ? null : seed / 2,
        Crc32 = isDirectory ? null : (uint)seed,
        Modified = new DateTime(2020, 1, 1).AddMinutes(seed),
        ModifiedHasTime = seed % 3 != 0,
        Encryption = seed % 5 == 0 ? EntryEncryption.Aes256 : EntryEncryption.None,
    };
}
