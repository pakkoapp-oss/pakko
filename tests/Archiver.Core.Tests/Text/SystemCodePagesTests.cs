using System.Text;
using Archiver.Core.Services.Sandbox;
using Archiver.Core.Services.Zip;
using Archiver.Core.Text;
using FluentAssertions;

namespace Archiver.Core.Tests.Text;

// T-F441: the one rule that turns a code page into an Encoding, and the boundaries that take their
// pages from it, checked with explicit pages so nothing depends on this machine's own.
public sealed class SystemCodePagesTests
{
    private static readonly string ECafe = "caf" + (char)0xE9;
    private static readonly string Zhe = ((char)0x416).ToString();
    private static readonly string Sun = ((char)0x65E5).ToString();

    [Fact]
    public void For_Utf8_HasNoByteOrderMark()
    {
        SystemCodePages.For(SystemCodePages.Utf8).GetPreamble().Should().BeEmpty();
        SystemCodePages.ForStrict(SystemCodePages.Utf8).GetPreamble().Should().BeEmpty();
    }

    [Theory]
    [InlineData(437)]
    [InlineData(866)]
    [InlineData(932)]
    [InlineData(1251)]
    [InlineData(1252)]
    public void For_WindowsCodePage_IsThatPage(int codePage)
    {
        SystemCodePages.For(codePage).CodePage.Should().Be(codePage);
    }

    // Before T-F441 an unknown page fell back to Latin-1 without a word; a wrong decoding then
    // looked like a name.
    [Theory]
    [InlineData(1)]
    [InlineData(99999)]
    public void For_UnknownCodePage_ThrowsNamingIt(int codePage)
    {
        Action act = () => SystemCodePages.For(codePage);
        act.Should().Throw<NotSupportedException>().WithMessage($"*{codePage}*");
        Action strict = () => SystemCodePages.ForStrict(codePage);
        strict.Should().Throw<NotSupportedException>().WithMessage($"*{codePage}*");
    }

    [Fact]
    public void For_UnmappableCharacter_BecomesReplacement_ForStrict_Throws()
    {
        SystemCodePages.For(1251).GetString(SystemCodePages.For(1251).GetBytes(ECafe)).Should().NotBe(ECafe);
        Action act = () => SystemCodePages.ForStrict(1251).GetBytes(ECafe);
        act.Should().Throw<EncoderFallbackException>();
    }

    [Fact]
    public void MachinePages_HaveEncodings()
    {
        int[] pages = [SystemCodePages.Ansi, SystemCodePages.Oem, SystemCodePages.UserAnsi, SystemCodePages.UserOem];
        pages.Should().AllSatisfy(page => SystemCodePages.For(page).Should().NotBeNull());
    }

    // A Unicode-only locale names no page of its own: GetLocaleInfoEx answers CP_ACP/CP_OEMCP (0/1),
    // which no table holds, so the system's page stands in, as libarchive does.
    [Fact]
    public void UserPages_LocaleWithoutAPage_FallBackToTheSystemPages()
    {
        SystemCodePages.UserOemFor("hi-IN").Should().Be(SystemCodePages.Oem);
        SystemCodePages.UserAnsiFor("hi-IN").Should().Be(SystemCodePages.Ansi);
    }

    [Fact]
    public void UserPages_LocaleWithPages_AreItsOwn()
    {
        SystemCodePages.UserOemFor("uk-UA").Should().Be(866);
        SystemCodePages.UserAnsiFor("uk-UA").Should().Be(1251);
        SystemCodePages.UserAnsiFor("en-US").Should().Be(1252);
    }

    // Each boundary with an injected page: what passes on one page is refused on another.
    [Theory]
    [InlineData(1251, false, true, false)]
    [InlineData(1252, true, false, false)]
    [InlineData(932, false, true, true)]
    [InlineData(65001, true, true, true)]
    public void TarCommandLine_PerCodePage(int codePage, bool eAcute, bool zhe, bool sun)
    {
        TarCommandLineEncoding.IsRepresentable(ECafe + ".txt", (uint)codePage).Should().Be(eAcute);
        TarCommandLineEncoding.IsRepresentable(Zhe + ".txt", (uint)codePage).Should().Be(zhe);
        TarCommandLineEncoding.IsRepresentable(Sun + ".txt", (uint)codePage).Should().Be(sun);
    }

    // tar.exe prints names in the user locale's ANSI page, which need not be the system page its
    // command line uses: the same bytes read as two different names.
    [Fact]
    public void TarOutput_UserPageDiffersFromSystemPage_DecodesByTheUserPage()
    {
        byte[] printed = SystemCodePages.For(1251).GetBytes(Zhe + ".txt");

        TarOutputEncoding.ForCodePage(1251).GetString(printed).Should().Be(Zhe + ".txt");
        TarOutputEncoding.ForCodePage(1252).GetString(printed).Should().NotBe(Zhe + ".txt");
    }

    [Fact]
    public void ZipNames_FromCodePages_UseTheGivenPages()
    {
        ZipNameCodePages pages = ZipNameCodePages.FromCodePages(866, 1251);

        pages.Oem.CodePage.Should().Be(866);
        pages.Ansi.CodePage.Should().Be(1251);
        ZipNameCodePages.FromCodePages(65001, 65001).Ansi.GetPreamble().Should().BeEmpty();
    }
}
