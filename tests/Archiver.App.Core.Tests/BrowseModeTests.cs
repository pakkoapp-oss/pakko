using Archiver.Core.Models;
using FluentAssertions;

namespace Archiver.App.Core.Tests;

// T-F199 step 6: browse mode's location rules, the encryption banner, lock marks, and the guard
// that keeps "Close archive" from racing a listing or drill-in still in flight.
public sealed class BrowseModeTests
{
    [Fact]
    public void Location_InsideTopLevelZip_OffersEverything()
    {
        var state = BrowseLocationState.For(insideArchive: true, nested: false, isZip: true);

        state.Should().Be(new BrowseLocationState(
            ShowsExtractActions: true, ShowsOptions: true, ShowsTest: true, OffersDeleteAfter: true, ShowsOutsideInfo: false));
    }

    [Fact]
    public void Location_InsideTarFamily_NoTest()
    {
        // tar.exe has no test mode; Explorer's menu offers Test for ZIP only (T-F86).
        BrowseLocationState.For(insideArchive: true, nested: false, isZip: false).ShowsTest.Should().BeFalse();
    }

    [Fact]
    public void Location_NestedArchive_NoDeleteAfter()
    {
        // The nested archive is a temp copy: sending it to the Recycle Bin means nothing.
        var state = BrowseLocationState.For(insideArchive: true, nested: true, isZip: true);

        state.OffersDeleteAfter.Should().BeFalse();
        state.ShowsExtractActions.Should().BeTrue();
    }

    [Fact]
    public void Location_OutsideArchive_HidesArchiveActionsAndExplains()
    {
        var state = BrowseLocationState.For(insideArchive: false, nested: false, isZip: false);

        state.Should().Be(new BrowseLocationState(
            ShowsExtractActions: false, ShowsOptions: false, ShowsTest: false, OffersDeleteAfter: false, ShowsOutsideInfo: true));
    }

    private static ArchiveEntryInfo Entry(string path, EntryEncryption? kind, int? ae = null) =>
        new() { Path = path, Encryption = kind, AesVersion = ae };

    [Theory]
    [InlineData(EntryEncryption.Aes256, "AES-256")]
    [InlineData(EntryEncryption.Aes192, "AES-192")]
    [InlineData(EntryEncryption.Aes128, "AES-128")]
    [InlineData(EntryEncryption.ZipCrypto, "ZipCrypto")]
    public void Badge_NamedMethod_IsTheMethodName(EntryEncryption kind, string expected)
    {
        EncryptionSummary.Of([Entry("a", kind, 2)]).BadgeName.Should().Be(expected);
    }

    [Fact]
    public void Badge_UnnamedMethod_HasNoName()
    {
        EncryptionSummary.Of([Entry("a", EntryEncryption.Unknown)]).BadgeName.Should().BeNull();
    }

    [Fact]
    public void Notes_Aes2Archive_ExplainsPasswordAndEmptyCrc()
    {
        EncryptionSummary.Of([Entry("a", EntryEncryption.Aes256, 2)]).NoteKeys
            .Should().Equal("BrowseEncryptedPasswordNote", "BrowseEncryptedAe2Note");
    }

    [Fact]
    public void Notes_Ae1Archive_NoEmptyCrcNote()
    {
        EncryptionSummary.Of([Entry("a", EntryEncryption.Aes128, 1)]).NoteKeys
            .Should().Equal("BrowseEncryptedPasswordNote");
    }

    [Fact]
    public void Notes_ZipCryptoAnywhere_WarnsItIsWeak()
    {
        // The unnamed method ranks the badge lower, but the ZipCrypto entry still gets its warning.
        var summary = EncryptionSummary.Of(
            [Entry("a", EntryEncryption.ZipCrypto), Entry("b", EntryEncryption.Unknown), Entry("c", EntryEncryption.Aes256, 2)]);

        summary.NoteKeys.Should().Equal("BrowseEncryptedPasswordNote", "BrowseEncryptedAe2Note", "BrowseZipCryptoWeakNote");
    }

    [Fact]
    public void TreeIndex_CarriesEncryptionToTheRow_SynthesizedFolderIsPlain()
    {
        ArchiveTree index = ArchiveTreeIndex.Build(
            [Entry("dir/secret.txt", EntryEncryption.Aes256, 2), Entry("plain.txt", EntryEncryption.None), Entry("tar.txt", null)]);

        ArchiveEntryViewModel folder = index.At(string.Empty).Single(e => e.Name == "dir");
        folder.IsEncrypted.Should().BeFalse();
        index.At(string.Empty).Single(e => e.Name == "plain.txt").IsEncrypted.Should().BeFalse();
        index.At(string.Empty).Single(e => e.Name == "tar.txt").IsEncrypted.Should().BeFalse();
        index.At("dir").Single().IsEncrypted.Should().BeTrue();
    }

    [Fact]
    public void Work_InFlightUntilEveryScopeEnds()
    {
        var work = new BrowseWork();
        work.InFlight.Should().BeFalse();

        using (work.Begin())
        {
            using (work.Begin())
                work.InFlight.Should().BeTrue();
            work.InFlight.Should().BeTrue();
        }

        work.InFlight.Should().BeFalse();
    }

    [Fact]
    public void Work_ScopeEndsOnException()
    {
        var work = new BrowseWork();

        Action act = () =>
        {
            using (work.Begin())
                throw new InvalidOperationException();
        };

        act.Should().Throw<InvalidOperationException>();
        work.InFlight.Should().BeFalse();
    }

    [Fact]
    public void Work_RaisesChangedOnStartAndEnd()
    {
        var work = new BrowseWork();
        int raised = 0;
        work.Changed += () => raised++;

        using (work.Begin())
        using (work.Begin()) { }

        raised.Should().Be(2);
    }

    [Fact]
    public void Work_DisposingAScopeTwiceCountsOnce()
    {
        var work = new BrowseWork();
        IDisposable outer = work.Begin();
        IDisposable inner = work.Begin();

        inner.Dispose();
        inner.Dispose();

        work.InFlight.Should().BeTrue();
        outer.Dispose();
        work.InFlight.Should().BeFalse();
    }

    // T-F275 step 3c: an archive kept open although its listing failed, because PAR2 files lie
    // next to it. Nothing to extract or delete; the test is what it stays open for.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WithoutListing_OffersNothingButWhatTheLocationAlreadyShows(bool isZip)
    {
        var listed = BrowseLocationState.For(insideArchive: true, nested: false, isZip);

        listed.WithoutListing().Should().Be(new BrowseLocationState(
            ShowsExtractActions: false, ShowsOptions: false, ShowsTest: isZip, OffersDeleteAfter: false, ShowsOutsideInfo: false));
    }
}
