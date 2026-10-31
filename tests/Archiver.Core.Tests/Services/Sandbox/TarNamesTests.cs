using Archiver.Core.Services.Sandbox;
using FluentAssertions;

namespace Archiver.Core.Tests.Services.Sandbox;

// T-F441/T-F424: tar.exe lists a name outside the user's ANSI page by best fit ("cafe" for the
// e-acute name, bsdtar 3.8.9 and 3.5.2) or as '?' (3.5.2). The verdict decides whether listed
// names may decide a selection, a filter or a duplicate group (docs/DECISIONS.md, "T-F441 and T-F424").
public sealed class TarNamesTests
{
    private static readonly string Accented = "caf" + (char)0xE9 + ".txt";

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
        TarNames.JudgeExtracted(listed, ["cafe.txt"]).Should().Be(TarListingVerdict.Faithful);

        Action act = () => TarNames.EnsureNamesCanDecide(TarNames.Judge(listed, null, ["cafe.txt"]));

        act.Should().Throw<TarArgumentEncodingException>();
    }

    [Fact]
    public void JudgeExtracted_FileTheListingDoesNotAccountFor_Lossy()
    {
        TarNames.JudgeExtracted(["cafe.txt", "cafe.txt"], [Accented, "cafe.txt"])
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
        TarNames.JudgeExtracted([listed], [onDisk]).Should().Be(TarListingVerdict.Faithful);
    }

    [Fact]
    public void JudgeExtracted_FoldersAndRealDuplicates_Faithful()
    {
        TarNames.JudgeExtracted(["d/", "d/a.txt", "d/a.txt"], ["d/a.txt"]).Should().Be(TarListingVerdict.Faithful);
        TarNames.JudgeExtracted(["a.txt", "b.txt"], []).Should().Be(TarListingVerdict.Faithful);
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
