using Archiver.Core.Services.Sandbox;
using FluentAssertions;

namespace Archiver.Core.Tests.Services.Sandbox;

// T-F441/T-F424: tar.exe lists a name outside the user's ANSI page by best fit ("cafe" for the
// e-acute name, bsdtar 3.8.9 and 3.5.2) or as '?' (3.5.2). The verdict decides whether listed
// names may decide a selection, a filter or a duplicate group (docs/DECISIONS.md, "T-F441 and T-F424").
public sealed class TarNamesTests
{
    private static readonly string Accented = "caf" + (char)0xE9 + ".txt";

    // T-F466: tar.exe lists and writes a name in NFC (measured on 3.8.9 and 3.5.2); a macOS tar
    // stores it in NFD.
    private static readonly string AccentedNfd = "cafe" + (char)0x301 + ".txt";

    [Fact]
    public void Judge_HeaderInNfdListedInNfc_Faithful()
    {
        TarNames.Judge([Accented, "plain.txt"], [AccentedNfd, "plain.txt"]).Should().Be(TarListingVerdict.Faithful);
        TarNames.Judge([Accented, "plain.txt"], [AccentedNfd, "plain.txt"], [Accented]).Should().Be(TarListingVerdict.Faithful);
    }

    // Both forms list as one name, and tar.exe's "-T" takes both entries (measured): the name
    // cannot pick either.
    [Fact]
    public void Judge_NfcAndNfdOfOneName_Lossy()
    {
        TarNames.Judge([Accented, Accented, "plain.txt"], [Accented, AccentedNfd, "plain.txt"])
            .Should().Be(new TarListingVerdict(TarNameTrust.Lossy, Accented));
        TarNames.Judge([Accented, Accented, "plain.txt"], [Accented, AccentedNfd, "plain.txt"], [Accented]).Trust
            .Should().Be(TarNameTrust.Lossy);
        TarNames.Judge([Accented, Accented, "plain.txt"], [Accented, AccentedNfd, "plain.txt"], ["plain.txt"])
            .Should().Be(TarListingVerdict.Faithful);
    }

    [Fact]
    public void Judge_NfdFolderListedInNfc_Faithful()
    {
        string folderNfc = "caf" + (char)0xE9 + "/";
        string folderNfd = "cafe" + (char)0x301;
        TarNames.Judge([folderNfc, folderNfc + "a.txt"], [folderNfd, folderNfd + "/a.txt"]).Should().Be(TarListingVerdict.Faithful);
    }

    [Fact]
    public void Judge_QuestionMark_Lossy()
    {
        TarListingVerdict verdict = TarNames.Judge(["./????.txt", "b.txt"], null);

        verdict.Should().Be(new TarListingVerdict(TarNameTrust.Lossy, "????.txt"));
    }

    [Fact]
    public void Judge_HeadersAgree_Faithful()
    {
        TarNames.Judge(["dir/", "dir/a.txt", Accented], ["dir", "dir/a.txt", Accented])
            .Should().Be(TarListingVerdict.Faithful);
    }

    // The case the user meets: an entry listed by best fit.
    [Fact]
    public void Judge_HeaderHoldsTheTrueName_Lossy()
    {
        TarNames.Judge(["cafe.txt", "cafe.txt"], [Accented, "cafe.txt"])
            .Should().Be(new TarListingVerdict(TarNameTrust.Lossy, "cafe.txt"));
    }

    [Fact]
    public void Judge_HeaderCountDiffers_Suspect()
    {
        TarNames.Judge(["a.txt"], ["a.txt", "b.txt"]).Trust.Should().Be(TarNameTrust.Suspect);
    }

    // T-F171: one name stored twice in a plain tar is a real duplicate, and the headers say so.
    [Fact]
    public void Judge_RepeatedNameTheHeadersConfirm_Faithful()
    {
        TarNames.Judge(["a.txt", "a.txt"], ["a.txt", "a.txt"]).Should().Be(TarListingVerdict.Faithful);
    }

    [Fact]
    public void Judge_NoHeaders_RepeatedName_Suspect()
    {
        TarNames.Judge(["cafe.txt", "cafe.txt"], null)
            .Should().Be(new TarListingVerdict(TarNameTrust.Suspect, "cafe.txt"));
    }

    [Fact]
    public void Judge_NoHeaders_DistinctNames_Faithful()
    {
        TarNames.Judge(["a.txt", "A.txt", "d/", "d/a.txt"], null).Should().Be(TarListingVerdict.Faithful);
    }

    // A selection decides only about its members: an unrelated unshowable name does not refuse it.
    [Fact]
    public void Judge_Selection_IgnoresNamesItDoesNotSelect()
    {
        TarNames.Judge(["????.txt", "b.txt"], null, ["b.txt"]).Should().Be(TarListingVerdict.Faithful);
        TarNames.Judge(["x.txt", "cafe.txt"], ["y.txt", "cafe.txt"], ["cafe.txt"]).Should().Be(TarListingVerdict.Faithful);
    }

    [Fact]
    public void Judge_Selection_OfACollapsedName_IsNotFaithful()
    {
        TarNames.Judge(["cafe.txt", "cafe.txt"], null, ["cafe.txt"]).Trust.Should().Be(TarNameTrust.Suspect);
        TarNames.Judge(["cafe.txt", "cafe.txt"], [Accented, "cafe.txt"], ["cafe.txt"]).Trust.Should().Be(TarNameTrust.Lossy);
    }

    // The headline: a selection in a format without readable headers (.tar.xz, 7z, RAR). The files on
    // disk would agree - only the real "cafe.txt" is extracted - so nothing after extraction may
    // clear it; it is refused before.
    [Fact]
    public void Selection_CollapsedNameWithoutHeaders_RefusedEvenThoughTheDiskWouldAgree()
    {
        string[] listed = ["cafe.txt", "cafe.txt"];
        TarNames.JudgeExtracted(listed, ["cafe.txt"], []).Should().Be(TarListingVerdict.Faithful);

        Action act = () => TarNames.EnsureNamesCanDecide(TarNames.Judge(listed, null, ["cafe.txt"]));

        act.Should().Throw<TarArgumentEncodingException>();
    }

    [Fact]
    public void JudgeExtracted_FileTheListingDoesNotAccountFor_Lossy()
    {
        TarNames.JudgeExtracted(["cafe.txt", "cafe.txt"], [Accented, "cafe.txt"], [])
            .Should().Be(new TarListingVerdict(TarNameTrust.Lossy, Accented));
    }

    // Names that land on disk differently by design must not read as lossy.
    [Theory]
    [InlineData("a?b.txt", "a_b.txt")]
    [InlineData("./x.txt", "x.txt")]
    [InlineData("././d/x.txt", "d/x.txt")]
    [InlineData("A.TXT", "a.txt")]
    [InlineData("a\\b.txt", "a/b.txt")]
    [InlineData("a.txt.", "a.txt.")]
    [InlineData("d//x.txt", "d/x.txt")]
    public void JudgeExtracted_NameWrittenDifferentlyByDesign_Faithful(string listed, string onDisk)
    {
        // T-F443: with every folder the file lies in, as the quarantine walk reports them.
        string[] folders = [.. onDisk.Split('/').SkipLast(1).Select((_, i) => string.Join('/', onDisk.Split('/').Take(i + 1)))];
        TarNames.JudgeExtracted([listed], [onDisk], folders).Should().Be(TarListingVerdict.Faithful);
    }

    [Fact]
    public void JudgeExtracted_FoldersAndRealDuplicates_Faithful()
    {
        TarNames.JudgeExtracted(["d/", "d/a.txt", "d/a.txt"], ["d/a.txt"], ["d"]).Should().Be(TarListingVerdict.Faithful);
        TarNames.JudgeExtracted(["a.txt", "b.txt"], [], []).Should().Be(TarListingVerdict.Faithful);
        TarNames.JudgeExtracted(["./d/", "./d/e/"], [], ["d", "D/E"]).Should().Be(TarListingVerdict.Faithful);
        TarNames.JudgeExtracted(["d/e/f/x.txt"], ["d/e/f/x.txt"], ["d", "d/e", "d/e/f"]).Should().Be(TarListingVerdict.Faithful);
    }

    // T-F443: a folder listed by best fit, with no file in it, is seen by its folder alone.
    [Fact]
    public void JudgeExtracted_FolderTheListingDoesNotAccountFor_Lossy()
    {
        string accentedFolder = Accented[..^4];
        TarNames.JudgeExtracted(["cafe/", "cafe/empty/"], [], [accentedFolder, accentedFolder + "/empty"])
            .Should().Be(new TarListingVerdict(TarNameTrust.Lossy, accentedFolder));
    }

    // A folder only Pakko made (pre-created by a listed name) is predicted, not unaccounted.
    [Fact]
    public void JudgeExtracted_PreCreatedParentOfAListedFile_Faithful()
    {
        TarNames.JudgeExtracted(["d/x.txt"], ["d/x.txt"], ["d"]).Should().Be(TarListingVerdict.Faithful);
    }

    [Fact]
    public void EnsureNamesCanDecide_PerVerdict()
    {
        TarNames.EnsureNamesCanDecide(TarListingVerdict.Faithful);
        TarNames.EnsureNamesCanDecide(new TarListingVerdict(TarNameTrust.Suspect, "a.txt"), allowSuspect: true);

        Action suspect = () => TarNames.EnsureNamesCanDecide(new TarListingVerdict(TarNameTrust.Suspect, "a.txt"));
        Action lossy = () => TarNames.EnsureNamesCanDecide(new TarListingVerdict(TarNameTrust.Lossy, "cafe.txt"), allowSuspect: true);
        Action noExample = () => TarNames.EnsureNamesCanDecide(new TarListingVerdict(TarNameTrust.Suspect, null));

        suspect.Should().Throw<TarArgumentEncodingException>().WithMessage("*a.txt*");
        lossy.Should().Throw<TarArgumentEncodingException>().WithMessage("*cafe.txt*");
        noExample.Should().Throw<TarArgumentEncodingException>();
    }
}
