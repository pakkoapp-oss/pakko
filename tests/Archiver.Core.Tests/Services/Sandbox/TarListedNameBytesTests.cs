using System.Text;
using Archiver.Core.Services.Sandbox;
using Archiver.Core.Text;
using FluentAssertions;

namespace Archiver.Core.Tests.Services.Sandbox;

// T-F465: TarNames.ReadListedNameBytes - the bytes tar.exe printed for each listed name, the only
// bytes a name sent back to tar.exe may have.
public sealed class TarListedNameBytesTests
{
    private static readonly Encoding Cyrillic = SystemCodePages.For(1251);
    private static readonly Encoding Western = SystemCodePages.For(1252);
    private static readonly Encoding Utf8 = SystemCodePages.For(SystemCodePages.Utf8);

    [Fact]
    public void PlainNames_TheirOwnBytes()
    {
        Dictionary<string, byte[]?> listed = TarNames.ReadListedNameBytes("a.txt\r\nsub/\nsub/b.txt\n"u8.ToArray(), Western);

        listed.Should().ContainKeys("a.txt", "sub", "sub/b.txt");
        listed["a.txt"].Should().Equal("a.txt"u8.ToArray());
        listed["sub"].Should().Equal("sub"u8.ToArray());
    }

    [Fact]
    public void EscapedByte_ReadAsTheByteItStandsFor()
    {
        // bsdtar escapes a byte it finds unprintable: "a\210" is a, 0x88 (measured on 3.8.9, 1251).
        Dictionary<string, byte[]?> listed = TarNames.ReadListedNameBytes("a\\210\n"u8.ToArray(), Cyrillic);

        listed.Should().ContainSingle().Which.Value.Should().Equal([(byte)'a', 0x88]);
    }

    // The measured swap on 1251: tar.exe printed a, 0x80 (DJE); read as 1252 that is "a" plus the euro
    // sign, which 1251 sends as a, 0x88 - another entry. The reader keeps what was printed, so the
    // scope sees the two differ.
    [Fact]
    public void WrongPage_KeepsThePrintedBytes()
    {
        Dictionary<string, byte[]?> listed = TarNames.ReadListedNameBytes([(byte)'a', 0x80, (byte)'\n'], Western);

        listed.Should().ContainSingle().Which.Value.Should().Equal([(byte)'a', 0x80]);
        listed.Keys.Single().Should().Be("a" + Western.GetString([0x80]));
        Cyrillic.GetBytes(listed.Keys.Single()).Should().Equal([(byte)'a', 0x88]);
    }

    [Fact]
    public void LossyLine_NameCanNeverBeSent()
    {
        // 0xFF is no UTF-8 (ACP 65001): decoding it loses the byte.
        Dictionary<string, byte[]?> listed = TarNames.ReadListedNameBytes([(byte)'a', 0xFF, (byte)'\n'], Utf8);

        listed.Should().ContainSingle().Which.Value.Should().BeNull();
    }

    [Fact]
    public void SameNameFromALossyAndALosslessLine_NameCanNeverBeSent()
    {
        byte[] listing = [.. Utf8.GetBytes("a" + Utf8.GetString([0xFF])), (byte)'\n', (byte)'a', 0xFF, (byte)'\n'];

        Dictionary<string, byte[]?> listed = TarNames.ReadListedNameBytes(listing, Utf8);

        listed.Should().ContainSingle().Which.Value.Should().BeNull();
    }

    [Fact]
    public void RepeatedName_SameBytes_Kept()
    {
        Dictionary<string, byte[]?> listed = TarNames.ReadListedNameBytes("a.txt\na.txt\n"u8.ToArray(), Western);

        listed["a.txt"].Should().Equal("a.txt"u8.ToArray());
    }

    [Fact]
    public void EmptyAndBadLines_Skipped()
    {
        // "\q" is no escape bsdtar writes: the service refuses such a listing before sending anything.
        Dictionary<string, byte[]?> listed = TarNames.ReadListedNameBytes("\n\r\nbad\\q\nok\n"u8.ToArray(), Western);

        listed.Keys.Should().BeEquivalentTo(["ok"]);
    }

    [Fact]
    public void NoTrailingLineBreak_LastLineRead()
    {
        TarNames.ReadListedNameBytes("only"u8.ToArray(), Western).Keys.Should().BeEquivalentTo(["only"]);
    }

    [Fact]
    public void EmptyListing_Empty()
    {
        TarNames.ReadListedNameBytes([], Western).Should().BeEmpty();
    }
}
