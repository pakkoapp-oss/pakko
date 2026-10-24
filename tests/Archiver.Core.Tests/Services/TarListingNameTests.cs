using System.Text;
using Archiver.Core.Services;
using Archiver.Core.Services.Sandbox;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

/// <summary>
/// T-F418: tar.exe prints each name through bsdtar's safe_fprintf/safe_fputs (tar/util.c), which
/// writes '\' as "\\", seven controls as "\a \b \f \n \r \t \v" and any other unprintable byte as
/// "\ooo". The listing reads the name back exactly; the lines measured on bsdtar 3.8.9 are the cases.
/// </summary>
public sealed class TarListingNameTests
{
    private static readonly Encoding Cp1251 = TarOutputEncoding.ForCodePage(1251);
    private static readonly Encoding Utf8 = TarOutputEncoding.ForCodePage(65001);

    [Theory]
    [InlineData(@"plain.txt", @"plain.txt")]
    [InlineData(@"a\\b.txt", @"a\b.txt")]
    [InlineData(@"x\\\\y.txt", @"x\\y.txt")]
    [InlineData(@"end\\", @"end\")]
    [InlineData(@"oct\\101.txt", @"oct\101.txt")]
    [InlineData(@"n\\n.txt", @"n\n.txt")]
    [InlineData(@"star*.txt", @"star*.txt")]
    [InlineData(@"sp ace.txt", @"sp ace.txt")]
    public void Unescape_ReadsBackslashesAsTheNameHasThem(string listed, string expected)
    {
        TarListingName.Unescape(listed, Cp1251).Should().Be(expected);
    }

    [Fact]
    public void Unescape_ReadsTheNamedControls()
    {
        TarListingName.Unescape(@"nl\nx\rbel\a\b\v\f\t.txt", Cp1251)
            .Should().Be("nl" + (char)0x0A + "x" + (char)0x0D + "bel" + (char)0x07 + (char)0x08 + (char)0x0B + (char)0x0C + (char)0x09 + ".txt");
    }

    [Theory]
    [InlineData(@"ctl\001.txt", 0x01)]
    [InlineData(@"del\177.txt", 0x7F)]
    [InlineData(@"esc\033.txt", 0x1B)]
    public void Unescape_ReadsAnOctalByte(string listed, int code)
    {
        string prefix = listed[..listed.IndexOf('\\')];
        TarListingName.Unescape(listed, Cp1251).Should().Be(prefix + (char)code + ".txt");
    }

    [Fact]
    public void Unescape_DecodesARunOfOctalBytesInTheListingsCodePage()
    {
        // U+0085 (NEL) is not printable: under a UTF-8 locale bsdtar writes its two bytes C2 85.
        TarListingName.Unescape(@"nel\302\205.txt", Utf8).Should().Be("nel" + (char)0x85 + ".txt");
        // 0xC6 in code page 1251 is U+0416.
        TarListingName.Unescape(@"\306.txt", Cp1251).Should().Be((char)0x416 + ".txt");
    }

    [Theory]
    [InlineData(@"bad\q.txt")]
    [InlineData(@"trailing\")]
    [InlineData(@"short\01")]
    [InlineData(@"short\01x")]
    [InlineData(@"big\400.txt")]
    [InlineData(@"eight\018.txt")]
    public void Unescape_RefusesWhatBsdtarNeverWrites(string listed)
    {
        TarListingName.Unescape(listed, Cp1251).Should().BeNull();
    }

    [Fact]
    public void ListedNames_SplitsTheLinesFirst_ThenReadsEachName()
    {
        // An escaped "\n" is inside one line; tar.exe ends each line with CR LF.
        string stdOut = "a\\\\b.txt\r\nnl\\nx.txt\r\ndir/\r\n";

        TarSandboxedService.ListedNames(stdOut, Cp1251)
            .Should().Equal(@"a\b.txt", "nl" + (char)0x0A + "x.txt", "dir/");
    }

    [Fact]
    public void ListedNames_IsNullWhenALineHoldsAnUnknownEscape()
    {
        TarSandboxedService.ListedNames("ok.txt\r\nbad\\q.txt\r\n", Cp1251).Should().BeNull();
    }

    [Theory]
    [InlineData(@"a\b.txt", @"a\\b.txt")]
    [InlineData(@"end\", @"end\\")]
    [InlineData(@"br[1]\x", @"br[[]1]\\x")]
    public void EscapeMemberPattern_DoublesABackslash(string name, string expected)
    {
        // Measured on bsdtar 3.8.9: a "-T" line "a\\b.txt" names a\b.txt, "a\b.txt" names ab.txt,
        // "a[\]b.txt" names nothing (inside a class '\' escapes the ']').
        TarSandboxScope.EscapeMemberPattern(name).Should().Be(expected);
    }
}
