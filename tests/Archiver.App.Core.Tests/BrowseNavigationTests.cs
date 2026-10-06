using FluentAssertions;

namespace Archiver.App.Core.Tests;

// T-F112: where the Archive Browser's Up goes (T-F107 climb, T-F98 nested levels).
public sealed class BrowseNavigationTests
{
    // T-F319: an archive opened from a browsed real folder that fails to list leaves the user
    // in that folder; every other way in ends on the pending list, as before.
    [Fact]
    public void ListFailure_OpenedFromARealFolder_GoesBackToThatFolder() =>
        BrowseNavigation.DecideListFailure(wasBrowsing: true, ArchiveBrowseScope.RealFileSystem)
            .Should().Be(BrowseListFailureStep.BackToRealFolder);

    [Theory]
    [InlineData(false, ArchiveBrowseScope.Archive)]
    [InlineData(false, ArchiveBrowseScope.RealFileSystem)]
    [InlineData(false, ArchiveBrowseScope.ThisPc)]
    [InlineData(true, ArchiveBrowseScope.Archive)]
    [InlineData(true, ArchiveBrowseScope.ThisPc)]
    public void ListFailure_AnyOtherWayIn_GoesToThePendingList(bool wasBrowsing, ArchiveBrowseScope priorScope) =>
        BrowseNavigation.DecideListFailure(wasBrowsing, priorScope)
            .Should().Be(BrowseListFailureStep.PendingList);

    [Fact]
    public void Up_InsideArchiveFolder_GoesToParentFolder() =>
        BrowseNavigation.DecideUp(ArchiveBrowseScope.Archive, "docs/2026", nestedDepth: 2, @"C:\a\b.zip")
            .Should().Be(BrowseUpStep.ArchiveParentFolder);

    [Fact]
    public void Up_NestedArchiveRoot_PopsOneLevel() =>
        BrowseNavigation.DecideUp(ArchiveBrowseScope.Archive, "", nestedDepth: 1, @"C:\Temp\scope\inner.zip")
            .Should().Be(BrowseUpStep.PopNestedLevel);

    [Fact]
    public void Up_OuterArchiveRoot_GoesToContainingFolder() =>
        BrowseNavigation.DecideUp(ArchiveBrowseScope.Archive, "", nestedDepth: 0, @"C:\a\b.zip")
            .Should().Be(BrowseUpStep.ContainingFolder);

    [Fact]
    public void Up_OuterArchiveRoot_NoContainingFolder_GoesToThisPc() =>
        BrowseNavigation.DecideUp(ArchiveBrowseScope.Archive, "", nestedDepth: 0, archivePath: null)
            .Should().Be(BrowseUpStep.ThisPc);

    [Fact]
    public void Up_RealFolder_GoesToParent() =>
        BrowseNavigation.DecideUp(ArchiveBrowseScope.RealFileSystem, @"C:\a\b", nestedDepth: 0, archivePath: null)
            .Should().Be(BrowseUpStep.RealParentFolder);

    [Fact]
    public void Up_DriveRoot_GoesToThisPc() =>
        BrowseNavigation.DecideUp(ArchiveBrowseScope.RealFileSystem, @"C:\", nestedDepth: 0, archivePath: null)
            .Should().Be(BrowseUpStep.ThisPc);

    [Fact]
    public void Up_ThisPc_DoesNothing() =>
        BrowseNavigation.DecideUp(ArchiveBrowseScope.ThisPc, "", nestedDepth: 0, archivePath: null)
            .Should().Be(BrowseUpStep.None);
}
