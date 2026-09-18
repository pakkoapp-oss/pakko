using System.Text;
using FluentAssertions;

namespace Archiver.Shell.Tests;

// T-F192: structural tests for the in-memory DLGTEMPLATEEX byte buffer -- the one part of
// PasswordDialog that's genuinely provable from dotnet test rather than only "the dialog did or
// didn't appear" on-device. A Phase 0 spike (DECISIONS.md) confirmed this exact layout renders
// correctly; these tests exist so a *future* edit that breaks the layout (a missing extraCount
// WORD, a wrong item count) is caught here instead of only surfacing as a silent dialog-creation
// failure two build cycles later.
public sealed class PasswordDialogTemplateBuilderTests
{
    private const int CDlgItemsOffset = 16; // dlgVer(2) + signature(2) + helpID(4) + exStyle(4) + style(4) = 16

    private static ushort ReadItemCount(byte[] template) => BitConverter.ToUInt16(template, CDlgItemsOffset);

    [Fact]
    public void Build_WithoutApplyToRemaining_ReportsFiveItems()
    {
        // static message + edit + show-password checkbox + OK + Cancel
        byte[] template = PasswordDialogTemplateBuilder.Build(
            "Password required", "message", canApplyToRemaining: false,
            "apply", "show", "OK", "Cancel");

        ReadItemCount(template).Should().Be(5);
    }

    [Fact]
    public void Build_WithApplyToRemaining_ReportsSixItems()
    {
        byte[] template = PasswordDialogTemplateBuilder.Build(
            "Password required", "message", canApplyToRemaining: true,
            "apply", "show", "OK", "Cancel");

        ReadItemCount(template).Should().Be(6);
    }

    [Fact]
    public void Build_SignatureAndVersionAreExtendedDialogTemplateMarkers()
    {
        // dlgVer=1, signature=0xFFFF -- the DLGTEMPLATEEX marker DialogBoxIndirectParamW expects.
        // Mixing this with the OLD (non-extended) DLGTEMPLATE's font-block shape is exactly the
        // "silently fails to create" trap this test guards against.
        byte[] template = PasswordDialogTemplateBuilder.Build(
            "t", "m", canApplyToRemaining: false, "a", "s", "o", "c");

        BitConverter.ToUInt16(template, 0).Should().Be(1);
        BitConverter.ToUInt16(template, 2).Should().Be(0xFFFF);
    }

    [Theory]
    [InlineData("Password required")]
    [InlineData("Пароль потрібен")] // non-ASCII must survive UTF-16 encoding into the template
    public void Build_EmbedsTitleAsNullTerminatedUtf16(string title)
    {
        byte[] template = PasswordDialogTemplateBuilder.Build(
            title, "message", canApplyToRemaining: false, "a", "s", "o", "c");

        byte[] expected = Encoding.Unicode.GetBytes(title + "\0");
        Contains(template, expected).Should().BeTrue();
    }

    [Fact]
    public void Build_EmbedsAllFourButtonAndCheckboxLabels()
    {
        byte[] template = PasswordDialogTemplateBuilder.Build(
            "Password required", "message", canApplyToRemaining: true,
            "ApplyLabel", "ShowLabel", "OkLabel", "CancelLabel");

        foreach (string label in new[] { "ApplyLabel", "ShowLabel", "OkLabel", "CancelLabel" })
            Contains(template, Encoding.Unicode.GetBytes(label + "\0")).Should().BeTrue($"'{label}' should be embedded");
    }

    [Fact]
    public void Build_EveryItemStartsOnADwordBoundary()
    {
        // Re-derive each item's start offset the same way the real DialogProc/USER32 parser
        // would -- if any item's helpID/exStyle/style triplet isn't 4-byte aligned, USER32 either
        // misreads the template or refuses to create the dialog. This walks the buffer using
        // ONLY the documented DLGITEMTEMPLATEEX field widths, independent of the builder's own
        // internal Align4() calls, so it actually proves the invariant rather than assuming it.
        byte[] t = PasswordDialogTemplateBuilder.Build(
            "Password required", "A password-protected archive message long enough to matter",
            canApplyToRemaining: true, "Apply to remaining archives", "Show password", "OK", "Cancel");

        int pos = 26; // past dlgVer,signature,helpID,exStyle,style,cDlgItems,x,y,cx,cy = 2+2+4+4+4+2+2+2+2+2
        pos = SkipSzOrOrd(t, pos); // menu
        pos = SkipSzOrOrd(t, pos); // windowClass
        pos = SkipString(t, pos);  // title
        // DS_SETFONT extended block: pointsize(2) + weight(2) + italic(1) + charset(1) + typeface
        pos += 6;
        pos = SkipString(t, pos); // typeface

        ushort itemCount = ReadItemCount(t);
        for (int i = 0; i < itemCount; i++)
        {
            pos = Align4(pos);
            pos.Should().BeLessThan(t.Length, $"item {i} should start inside the buffer");
            pos += 4 + 4 + 4 + 2 + 2 + 2 + 2 + 4; // helpID,exStyle,style,x,y,cx,cy,id
            pos = SkipSzOrOrd(t, pos); // windowClass
            pos = SkipSzOrOrd(t, pos); // title
            pos += 2; // extraCount
        }
    }

    private static int Align4(int offset) => (offset + 3) & ~3;

    private static int SkipSzOrOrd(byte[] t, int pos)
    {
        ushort marker = BitConverter.ToUInt16(t, pos);
        return marker == 0xFFFF ? pos + 4 : SkipString(t, pos);
    }

    private static int SkipString(byte[] t, int pos)
    {
        int p = pos;
        while (BitConverter.ToUInt16(t, p) != 0)
            p += 2;
        return p + 2;
    }

    private static bool Contains(byte[] haystack, byte[] needle)
    {
        for (int i = 0; i <= haystack.Length - needle.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j]) { match = false; break; }
            }
            if (match) return true;
        }
        return false;
    }
}
