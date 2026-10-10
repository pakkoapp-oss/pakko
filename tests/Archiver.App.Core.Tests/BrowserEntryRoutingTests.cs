using FluentAssertions;

namespace Archiver.App.Core.Tests;

// T-F242 items 3, 4 and 6.
public sealed class BrowserEntryRoutingTests
{
    private static Func<bool> Archive(bool value) => () => value;

    private static Func<bool> MustNotProbe => () => throw new InvalidOperationException("disk probe not expected");

    [Fact]
    public void PendingRow_RealArchive_Opens() =>
        BrowserEntryRouting.DecidePendingRow(isBusy: false, isFolder: false, Archive(true)).Should().Be(RowOpenAction.OpenArchive);

    [Fact]
    public void PendingRow_WhileBusy_DoesNothingAndDoesNotProbe() =>
        BrowserEntryRouting.DecidePendingRow(isBusy: true, isFolder: false, MustNotProbe).Should().Be(RowOpenAction.None);

    [Fact]
    public void PendingRow_Folder_DoesNothing() =>
        BrowserEntryRouting.DecidePendingRow(isBusy: false, isFolder: true, MustNotProbe).Should().Be(RowOpenAction.None);

    [Fact]
    public void PendingRow_NotAnArchive_DoesNothing() =>
        BrowserEntryRouting.DecidePendingRow(isBusy: false, isFolder: false, Archive(false)).Should().Be(RowOpenAction.None);

    [Theory]
    [InlineData(true, "docs", true, RowOpenAction.OpenFolder)]
    [InlineData(false, "docs", true, RowOpenAction.OpenFolder)]
    [InlineData(true, "inner.zip", false, RowOpenAction.DrillIntoNestedArchive)]
    [InlineData(true, "photo.jpg", false, RowOpenAction.Preview)]
    [InlineData(true, "setup.exe", false, RowOpenAction.ExtractWithWarning)]
    public void BrowserRow_Inside(bool insideArchive, string name, bool isFolder, RowOpenAction expected) =>
        BrowserEntryRouting.DecideBrowserRow(false, insideArchive, isFolder, name, MustNotProbe).Should().Be(expected);

    [Theory]
    [InlineData(true, RowOpenAction.OpenArchive)]
    [InlineData(false, RowOpenAction.None)]
    public void BrowserRow_OutsideArchive_OnlyARealArchiveOpens(bool isArchive, RowOpenAction expected) =>
        BrowserEntryRouting.DecideBrowserRow(false, insideArchive: false, isFolder: false, "report.dat", Archive(isArchive))
            .Should().Be(expected);

    [Fact]
    public void BrowserRow_WhileBusy_DoesNothing() =>
        BrowserEntryRouting.DecideBrowserRow(true, true, true, "docs", MustNotProbe).Should().Be(RowOpenAction.None);

    [Theory]
    [InlineData("a/b.txt", @"a\b.txt")]
    [InlineData("b.txt", "b.txt")]
    public void ResolveInScope_NormalEntry_InsideScope(string entry, string expectedRelative)
    {
        string scope = Path.Combine(Path.GetTempPath(), "scope");

        BrowserEntryRouting.ResolveInScope(scope, entry).Should().Be(Path.Combine(scope, expectedRelative));
    }

    [Theory]
    [InlineData("C:/Windows/win.ini")]
    [InlineData(@"C:\Windows\win.ini")]
    [InlineData("/Windows/win.ini")]
    [InlineData("../outside.txt")]
    [InlineData("a/../../outside.txt")]
    [InlineData("..")]
    [InlineData("")]
    public void ResolveInScope_EscapingEntry_Null(string entry)
    {
        string scope = Path.Combine(Path.GetTempPath(), "scope");

        BrowserEntryRouting.ResolveInScope(scope, entry).Should().BeNull();
    }

    [Fact]
    public void ResolveInScope_SiblingWithScopeNamePrefix_Null()
    {
        string scope = Path.Combine(Path.GetTempPath(), "scope");

        BrowserEntryRouting.ResolveInScope(scope, "../scope2/x.txt").Should().BeNull();
    }

    // T-F275 step 3c: a .par2 row on disk opens the archive it protects.
    [Theory]
    [InlineData("photos.zip.par2")]
    [InlineData("PHOTOS.VOL0+1.PAR2")]
    [InlineData(@"C:\sets\photos.par2")]
    public void Par2RowOnDisk_OpensTheProtectedArchive(string name)
    {
        BrowserEntryRouting.OpensProtectedArchive(isBusy: false, insideArchive: false, isFolder: false, name).Should().BeTrue();
        // The ordinary routing leaves such a row alone, so the two never both act on it.
        BrowserEntryRouting.DecidePendingRow(isBusy: false, isFolder: false, Archive(false)).Should().Be(RowOpenAction.None);
        BrowserEntryRouting.DecideBrowserRow(isBusy: false, insideArchive: false, isFolder: false, name, Archive(false)).Should().Be(RowOpenAction.None);
    }

    [Theory]
    [InlineData(true, false, false, "a.zip.par2")]  // an operation is running
    [InlineData(false, true, false, "a.zip.par2")]  // an entry inside an archive is an ordinary entry
    [InlineData(false, false, true, "a.zip.par2")]  // a folder named like one
    [InlineData(false, false, false, "a.zip")]
    [InlineData(false, false, false, "a.par2.bak")]
    [InlineData(false, false, false, "par2")]
    [InlineData(false, false, false, "")]
    public void Par2Row_Otherwise_IsLeftAlone(bool isBusy, bool insideArchive, bool isFolder, string name) =>
        BrowserEntryRouting.OpensProtectedArchive(isBusy, insideArchive, isFolder, name).Should().BeFalse();
}
