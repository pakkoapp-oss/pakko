using System.Text;
using Archiver.Core.Services.Sandbox;
using Archiver.Core.Text;
using FluentAssertions;

namespace Archiver.Core.Tests.Services.Sandbox;

// T-F467, measured on PakkoW10 (system ja-JP, 932, bsdtar 3.5.2): tar.exe prints a raw header name in
// the byte form the archive stored, NEC's 0x87 0x90 for U+2252 among them (shown as "\\207\\220"), and
// matches a "-T" name as text: the canonical 0x81 0xE0 Pakko sends took that entry. An archive with
// both forms of one name lists them alike, and "-T" takes both.
public sealed class TarSendsAsListedTests
{
    private static readonly Encoding Japanese = new TextBoundary(932).Lenient;
    private const uint JapanesePage = 932;
    private static readonly string Nearly = "a" + (char)0x2252 + ".t";

    [Fact]
    public void ListedNecForm_IsReadAsItsName()
    {
        Dictionary<string, byte[]?> listed = TarNames.ReadListedNameBytes("a\\207\\220.t\r\nplain.t\r\n"u8.ToArray(), Japanese);

        listed[Nearly].Should().Equal([(byte)'a', 0x87, 0x90, (byte)'.', (byte)'t']);
    }

    // tar.exe printed NEC's selection of IBM's extension (0xED 0x40, U+7E8A) as raw bytes, not escaped;
    // the page's own form is IBM's 0xFA 0x5C.
    [Fact]
    public void RawNecSelectedIbmForm_IsReadAsItsName_AndTheCanonicalFormReachesIt()
    {
        byte[] line = [(byte)'c', 0xED, 0x40, (byte)'.', (byte)'t', 0x0A];
        Dictionary<string, byte[]?> listed = TarNames.ReadListedNameBytes(line, Japanese);
        string name = "c" + (char)0x7E8A + ".t";

        listed[name].Should().Equal([(byte)'c', 0xED, 0x40, (byte)'.', (byte)'t']);
        Reaches(name, listed, JapanesePage, [(byte)'c', 0xFA, 0x5C, (byte)'.', (byte)'t']).Should().BeTrue();
    }

    [Fact]
    public void BothRawFormsListed_NeitherIsReached()
    {
        byte[] listing = [(byte)'c', 0xED, 0x40, (byte)'.', (byte)'t', 0x0A, (byte)'c', 0xFA, 0x5C, (byte)'.', (byte)'t', 0x0A];
        Dictionary<string, byte[]?> listed = TarNames.ReadListedNameBytes(listing, Japanese);

        Reaches("c" + (char)0x7E8A + ".t", listed, JapanesePage, [(byte)'c', 0xFA, 0x5C, (byte)'.', (byte)'t']).Should().BeFalse();
    }

    // 0x5C after a lead byte is the character's second byte (0x83 0x5C, katakana SO), not tar's escape.
    [Fact]
    public void BackslashAsSecondByte_IsNotAnEscape()
    {
        byte[] line = [(byte)'s', 0x83, 0x5C, (byte)'n', 0x0A];
        Dictionary<string, byte[]?> listed = TarNames.ReadListedNameBytes(line, Japanese);

        listed["s" + (char)0x30BD + "n"].Should().Equal([(byte)'s', 0x83, 0x5C, (byte)'n']);
    }

    [Fact]
    public void CanonicalBytes_ReachTheEntryPrintedInTheNecForm()
    {
        Dictionary<string, byte[]?> listed = TarNames.ReadListedNameBytes("a\\207\\220.t\r\nplain.t\r\n"u8.ToArray(), Japanese);

        Reaches(Nearly, listed, JapanesePage, [(byte)'a', 0x81, 0xE0, (byte)'.', (byte)'t']).Should().BeTrue();
    }

    [Fact]
    public void BothFormsListed_NeitherIsReached()
    {
        Dictionary<string, byte[]?> listed = TarNames.ReadListedNameBytes("a\\207\\220.t\r\na\\201\\340.t\r\n"u8.ToArray(), Japanese);

        Reaches(Nearly, listed, JapanesePage, [(byte)'a', 0x81, 0xE0, (byte)'.', (byte)'t']).Should().BeFalse();
    }

    // Read in another page the two forms are two names, yet in tar.exe's page they are one text, and
    // "-T" takes both: the text, not the listing's key, must be printed once.
    [Fact]
    public void TwoFormsOfOneTextUnderTwoKeys_NeitherIsReached()
    {
        Dictionary<string, byte[]?> listed = TarNames.ReadListedNameBytes("a\\207\\220.t\na\\201\\340.t\n"u8.ToArray(), new TextBoundary(1252).Lenient);
        listed.Should().HaveCount(2).And.OnlyContain(pair => pair.Value != null);
        string first = listed.Keys.First();

        Reaches(first, listed, JapanesePage, [(byte)'a', 0x81, 0xE0, (byte)'.', (byte)'t']).Should().BeFalse();
    }

    // T-F465's swap: the listing read in 1252 shows "a" + euro for the entry tar.exe printed as a, 0x80
    // in 1251; sent in 1251 that name is a, 0x88, another entry's text. The same text, not the same
    // page, decides.
    [Fact]
    public void ListingReadInAnotherPage_NotReached()
    {
        Dictionary<string, byte[]?> listed = TarNames.ReadListedNameBytes("a\\200\n"u8.ToArray(), new TextBoundary(1252).Lenient);
        string shown = "a" + (char)0x20AC;

        Reaches(shown, listed, 1251, [(byte)'a', 0x88]).Should().BeFalse();
    }

    [Fact]
    public void SameBytes_Reached_UnlessAnotherFormSharesTheText()
    {
        Dictionary<string, byte[]?> one = TarNames.ReadListedNameBytes("a\\201\\340.t\n"u8.ToArray(), Japanese);
        Reaches(Nearly, one, JapanesePage, [(byte)'a', 0x81, 0xE0, (byte)'.', (byte)'t']).Should().BeTrue();

        Reaches("missing", one, JapanesePage, "missing"u8.ToArray()).Should().BeFalse();
    }

    private static bool Reaches(string key, Dictionary<string, byte[]?> listed, uint page, byte[] sent) =>
        TarNames.ReachesTheListedEntry(key, listed, TarNames.IndexByText(listed, page), page, sent);
}
