using System.Text;

namespace Archiver.Shell;

/// <summary>
/// Builds an in-memory extended dialog template (DLGTEMPLATEEX + DLGITEMTEMPLATEEX[]) for
/// <see cref="PasswordDialog"/>'s <c>DialogBoxIndirectParamW</c> call — T-F192.
///
/// Why a hand-built byte template instead of a compiled .rc resource (the approach NanaZip's real
/// <c>PasswordDialog.rc</c> uses, confirmed via its actual source before this was written): this
/// project's hard constraint prefers a script/in-memory-struct approach over new build-pipeline
/// customization wherever one works, and <c>Archiver.Shell</c> is a plain C# project with no
/// existing rc.exe/native-resource-compilation step (unlike <c>Archiver.ShellExtension</c>'s C++
/// project). <see cref="ShellConflictDialog"/> already established the same "build the native
/// struct in memory, no resource file" pattern for <c>TaskDialogIndirect</c> — this mirrors it for
/// a classic <c>DIALOGEX</c>-shaped template instead.
///
/// Kept as a pure function returning <c>byte[]</c> — separate from <see cref="PasswordDialog"/>'s
/// P/Invoke body — specifically so byte-layout mistakes (a wrong DWORD-alignment padding, a
/// missing extraCount WORD, an off-by-one control count) are catchable by
/// <c>Archiver.Shell.Tests</c> instead of only surfacing as "the dialog didn't appear" on-device.
/// A Phase 0 spike (see DECISIONS.md's T-F192 entry) confirmed this exact layout actually renders
/// and round-trips real input before this was wired into any real extraction command.
/// </summary>
internal static class PasswordDialogTemplateBuilder
{
    // Control IDs read back by PasswordDialog's DialogProc.
    public const int IdEdit = 101;
    public const int IdApplyToRemaining = 102;
    public const int IdShowPassword = 103;

    // Style/class constants — public, stable Win32 ABI (MSDN "Extended Dialog Box Template",
    // "DLGITEMTEMPLATEEX", predefined dialog control classes). Not something that needs a
    // NanaZip-style "check a real example" pass on its own — it's the same documented SDK ABI
    // NanaZip's compiled .rc resolves down to; the real design question this file settles (custom
    // DIALOGEX vs CredUI) was answered by fetching NanaZip's actual PasswordDialog.rc/.cpp, see
    // DECISIONS.md.
    private const uint DS_SETFONT = 0x40;
    private const uint DS_MODALFRAME = 0x80;
    private const uint DS_CENTER = 0x0800;
    private const uint WS_POPUP = 0x80000000;
    private const uint WS_CAPTION = 0x00C00000;
    private const uint WS_SYSMENU = 0x00080000;
    private const uint WS_VISIBLE = 0x10000000;
    private const uint WS_CHILD = 0x40000000;
    private const uint WS_TABSTOP = 0x00010000;
    private const uint WS_BORDER = 0x00800000;
    private const uint WS_GROUP = 0x00020000;
    private const uint ES_PASSWORD = 0x0020;
    private const uint ES_AUTOHSCROLL = 0x0080;
    private const uint BS_AUTOCHECKBOX = 0x0003;
    private const uint BS_DEFPUSHBUTTON = 0x0001;
    private const uint BS_PUSHBUTTON = 0x0000;
    private const ushort ClassButton = 0x0080;
    private const ushort ClassEdit = 0x0081;
    private const ushort ClassStatic = 0x0082;

    public static byte[] Build(string title, string message, bool canApplyToRemaining,
        string applyToRemainingLabel, string showPasswordLabel, string okLabel, string cancelLabel)
    {
        var w = new List<byte>();

        void U16(ushort v) => w.AddRange(BitConverter.GetBytes(v));
        void I16(short v) => w.AddRange(BitConverter.GetBytes(v));
        void U32(uint v) => w.AddRange(BitConverter.GetBytes(v));
        void Str(string s) { w.AddRange(Encoding.Unicode.GetBytes(s)); U16(0); }
        void Align4() { while (w.Count % 4 != 0) w.Add(0); }
        void SzOrOrdEmpty() => U16(0x0000);
        void SzOrOrdAtom(ushort atom) { U16(0xFFFF); U16(atom); }

        // --- Header (DLGTEMPLATEEX) ---
        U16(1);      // dlgVer
        U16(0xFFFF); // signature
        U32(0);      // helpID
        U32(0);      // exStyle
        U32(DS_SETFONT | DS_MODALFRAME | DS_CENTER | WS_POPUP | WS_CAPTION | WS_SYSMENU); // style
        int cDlgItemsOffset = w.Count;
        U16(0); // cDlgItems placeholder, patched below once the real count is known
        I16(0); I16(0); I16(220); I16(110); // x, y, cx, cy (dialog units)
        SzOrOrdEmpty(); // menu
        SzOrOrdEmpty(); // windowClass
        Str(title);
        // DS_SETFONT block — the EXTENDED form carries pointsize + weight + italic + charset
        // before the typeface (the non-extended DLGTEMPLATE form only has pointsize and typeface —
        // mixing the two produces a dialog that silently fails to create).
        U16(9);   // pointsize
        U16(400); // weight (FW_NORMAL)
        w.Add(0); // italic = FALSE
        w.Add(1); // charset = DEFAULT_CHARSET
        Str("MS Shell Dlg");

        int realItemCount = 0;
        void AddItem(uint style, short x, short y, short cx, short cy, int id, ushort classAtom, string text) // NOSONAR: S107 — one parameter per DLGITEMTEMPLATEEX field, not a clustering candidate
        {
            Align4();
            U32(0); // helpID
            U32(0); // exStyle
            U32(style | WS_VISIBLE | WS_CHILD);
            I16(x); I16(y); I16(cx); I16(cy);
            U32((uint)id);
            SzOrOrdAtom(classAtom);
            if (text.Length == 0) SzOrOrdEmpty(); else Str(text);
            U16(0); // extraCount
            realItemCount++;
        }

        Align4();
        AddItem(0, 7, 7, 206, 28, 100, ClassStatic, message);
        AddItem(WS_TABSTOP | WS_BORDER | ES_AUTOHSCROLL | ES_PASSWORD, 7, 38, 206, 14, IdEdit, ClassEdit, "");
        short y = 56;
        if (canApplyToRemaining)
        {
            AddItem(WS_TABSTOP | BS_AUTOCHECKBOX, 7, y, 206, 10, IdApplyToRemaining, ClassButton, applyToRemainingLabel);
            y += 16;
        }
        AddItem(WS_TABSTOP | BS_AUTOCHECKBOX, 7, y, 206, 10, IdShowPassword, ClassButton, showPasswordLabel);
        y += 20;
        AddItem(WS_TABSTOP | WS_GROUP | BS_DEFPUSHBUTTON, 100, y, 50, 14, 1 /* IDOK */, ClassButton, okLabel);
        AddItem(WS_TABSTOP | BS_PUSHBUTTON, 156, y, 50, 14, 2 /* IDCANCEL */, ClassButton, cancelLabel);

        byte[] bytes = w.ToArray();
        BitConverter.GetBytes((ushort)realItemCount).CopyTo(bytes, cDlgItemsOffset);
        return bytes;
    }
}
