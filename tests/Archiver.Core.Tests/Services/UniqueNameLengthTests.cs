using System.IO.Compression;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F573: a numbered name ("name (1)") never passes NTFS's 255 units for one name; the stem gives
// way first, then the end of the extension, never half of a surrogate pair.
public sealed class UniqueNameLengthTests : IDisposable
{
    private const int MaxName = 255;

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static string Taken(string name, int upTo)
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { name };
        string next = name;
        for (int i = 1; i < upTo; i++)
        {
            next = ArchiveNaming.GetUniqueName(name, taken.Contains);
            taken.Add(next);
        }
        return ArchiveNaming.GetUniqueName(name, taken.Contains);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(100)]
    public void NameAtTheLimit_NumberedNameFits(int number)
    {
        string name = new string('n', MaxName - 4) + ".txt";

        string numbered = Taken(name, number);

        numbered.Length.Should().BeLessThanOrEqualTo(MaxName);
        numbered.Should().EndWith($" ({number}).txt").And.StartWith("nnn");
    }

    [Fact]
    public void LargeNumber_StillFits()
    {
        string name = new string('n', MaxName - 4) + ".txt";
        string candidate = ArchiveNaming.GetUniqueName(name, c => !c.Contains("(100000)", StringComparison.Ordinal));

        candidate.Should().EndWith(" (100000).txt");
        candidate.Length.Should().BeLessThanOrEqualTo(MaxName);
    }

    [Fact]
    public void CompoundExtension_StemGivesWay()
    {
        string name = new string('s', MaxName - 7) + ".tar.gz";

        string numbered = ArchiveNaming.GetUniqueName(name, c => c == name);

        numbered.Should().HaveLength(MaxName).And.EndWith(" (1).tar.gz");
    }

    // ".<254>" is all extension: its end gives way, and the name never ends in a dot or a space.
    [Theory]
    [InlineData('x')]
    [InlineData('.')]
    public void DotFile_ExtensionEndGivesWay(char filler)
    {
        string name = "." + new string('x', 200) + new string(filler, MaxName - 201);

        string numbered = ArchiveNaming.GetUniqueName(name, c => c == name);

        numbered.Length.Should().BeLessThanOrEqualTo(MaxName);
        numbered.Should().Contain(" (1)").And.NotEndWith(".").And.NotEndWith(" ");
        if (filler == 'x')
            numbered.Should().StartWith(" (1).xxx");
    }

    [Fact]
    public void CutNeverSplitsASurrogatePair()
    {
        string name = new string('n', MaxName - 6) + "\U0001F600.txt";

        string numbered = ArchiveNaming.GetUniqueName(name, c => c == name);

        numbered.Length.Should().BeLessThanOrEqualTo(MaxName);
        numbered.Should().EndWith(" (1).txt");
        char.IsHighSurrogate(numbered[^9]).Should().BeFalse();
    }

    [Fact]
    public void Folder_NumberedNameFits()
    {
        string name = new string('d', MaxName);
        Directory.CreateDirectory(@"\\?\" + Path.Combine(_temp.Path, name));

        string numbered = ArchiveNaming.GetUniqueFolderName(_temp.Path, name);

        numbered.Should().HaveLength(MaxName).And.EndWith(" (1)");
    }

    [Fact]
    public void EntryNameWithFolders_LastPartFits()
    {
        string name = "dir/" + new string('n', MaxName - 4) + ".txt";

        string numbered = ArchiveNaming.GetUniqueEntryName(name, c => c == name);

        numbered.Should().StartWith("dir/").And.EndWith(" (1).txt");
        (numbered.Length - "dir/".Length).Should().BeLessThanOrEqualTo(MaxName);
    }

    // ArchiveTempFile and RecoveryService name their outputs through this one call.
    [Fact]
    public void UniqueFilePath_NameAtTheLimit_Fits()
    {
        string path = Path.Combine(_temp.Path, new string('a', MaxName - 4) + ".zip");

        string unique = ArchiveNaming.GetUniqueFilePath(path);

        Path.GetFileName(unique).Should().HaveLength(MaxName).And.EndWith(" (1).zip");
    }

    // The ordinary path: an entry named at the limit, extracted twice with Rename.
    [Fact]
    public async Task EntryAtTheLimit_ExtractedTwiceWithRename_BothWritten()
    {
        string entryName = new string('n', MaxName - 4) + ".txt";
        string zip = Path.Combine(_temp.Path, "long.zip");
        using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(archive.CreateEntry(entryName).Open()))
            writer.Write("content");
        string dest = Path.Combine(_temp.Path, "out");

        ArchiveResult second = null!;
        for (int run = 0; run < 2; run++)
        {
            second = await new ZipArchiveService(new GroupPolicyOptions()).ExtractAsync(new ExtractOptions
            {
                ArchivePaths = [zip],
                DestinationFolder = dest,
                Mode = ExtractMode.SingleFolder,
                OnConflict = ConflictBehavior.Rename,
            });
        }

        second.Errors.Should().BeEmpty();
        Directory.EnumerateFiles(dest).Should().HaveCount(2).And.AllSatisfy(f => Path.GetFileName(f).Length.Should().BeLessThanOrEqualTo(MaxName));
    }
}
