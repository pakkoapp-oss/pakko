using FluentAssertions;

namespace Archiver.App.Core.Tests;

// T-F199 (option A: two buttons, the accent on the one that fits the list) and T-F212 (Extract
// was enabled for a list of folders).
public sealed class PrimaryActionPolicyTests
{
    private static bool CanOpen(string path) => path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".7z", StringComparison.OrdinalIgnoreCase);

    private static (string Path, bool IsFolder) File(string path) => (path, false);

    private static (string Path, bool IsFolder) Folder(string path) => (path, true);

    [Fact]
    public void EmptyList_NothingRunsAndCompressIsTheAccent()
    {
        ListActions actions = PrimaryActionPolicy.Evaluate([], CanOpen);

        actions.CanCompress.Should().BeFalse();
        actions.CanExtract.Should().BeFalse();
        actions.Accent.Should().Be(PrimaryAction.Compress);
        actions.ArchivesOnly.Should().BeFalse();
        actions.ExtractablePaths.Should().BeEmpty();
    }

    [Fact]
    public void OnlyFolders_ExtractDisabledWithReason()
    {
        ListActions actions = PrimaryActionPolicy.Evaluate([Folder(@"C:\s2"), Folder(@"C:\s4.zip")], CanOpen);

        actions.CanCompress.Should().BeTrue();
        actions.CanExtract.Should().BeFalse();
        actions.ExtractUnavailable.Should().BeTrue();
        actions.Accent.Should().Be(PrimaryAction.Compress);
    }

    [Fact]
    public void OnlyArchives_ExtractIsTheAccentAndTheNewArchiveCardCollapses()
    {
        ListActions actions = PrimaryActionPolicy.Evaluate([File(@"C:\a.zip"), File(@"C:\b.7z")], CanOpen);

        actions.CanExtract.Should().BeTrue();
        actions.CanCompress.Should().BeTrue();
        actions.Accent.Should().Be(PrimaryAction.Extract);
        actions.ArchivesOnly.Should().BeTrue();
        actions.ExtractablePaths.Should().Equal(@"C:\a.zip", @"C:\b.7z");
    }

    [Fact]
    public void Mixed_CompressIsTheAccent_ExtractTakesOnlyTheArchives()
    {
        ListActions actions = PrimaryActionPolicy.Evaluate([File(@"C:\doc.txt"), File(@"C:\a.zip"), Folder(@"C:\f")], CanOpen);

        actions.Accent.Should().Be(PrimaryAction.Compress);
        actions.CanExtract.Should().BeTrue();
        actions.ExtractUnavailable.Should().BeFalse();
        actions.ArchivesOnly.Should().BeFalse();
        actions.ExtractablePaths.Should().Equal(@"C:\a.zip");
    }

    [Fact]
    public void ArchiveThePolicyRefuses_DoesNotCount()
    {
        ListActions actions = PrimaryActionPolicy.Evaluate([File(@"C:\a.rar")], CanOpen);

        actions.CanExtract.Should().BeFalse();
        actions.ExtractUnavailable.Should().BeTrue();
        actions.Accent.Should().Be(PrimaryAction.Compress);
    }
}
