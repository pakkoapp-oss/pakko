using System.Runtime.InteropServices;
using Archiver.Core.Services.Sandbox;

namespace Archiver.Core.Tests.Services.Sandbox;

/// <summary>
/// T-F467: how bsdtar prints an entry name in a "-t" listing, transcribed from libarchive's
/// tar/util.c (safe_fprintf and bsdtar_expand_char, the same logic in v3.5.2 and v3.8.9), run through
/// the C runtime's own character functions for one code page. Checked against the real tar.exe by
/// TarListingModelTests (integration) and on PakkoW10 per page (docs/DECISIONS.md, "T-F467 done").
/// </summary>
internal sealed partial class BsdtarListingModel : IDisposable
{
    private const int LcAll = 0;
    private readonly IntPtr _locale;

    public BsdtarListingModel(int codePage)
    {
        string name = codePage == 65001 ? ".utf8" : "." + codePage.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _locale = CreateLocale(LcAll, name);
        if (_locale == IntPtr.Zero)
            throw new InvalidOperationException("the C runtime has no locale " + name);
    }

    public void Dispose() => FreeLocale(_locale);

    /// <summary>The bytes bsdtar prints for <paramref name="name"/>, without the line end.</summary>
    public byte[] Print(byte[] name)
    {
        var output = new List<byte>(name.Length * 4);
        int p = 0;
        bool tryWide = true;
        while (p < name.Length)
        {
            ushort wide = 0;
            int n = tryWide ? MbToWc(out wide, name[p..], (nuint)(name.Length - p), _locale) : -1;
            if (tryWide && n > 0)
            {
                bool printable = IsWPrint(wide, _locale) != 0 && wide != '\\';
                for (int k = 0; k < n; k++)
                {
                    if (printable)
                        output.Add(name[p]);
                    else
                        Expand(output, name[p]);
                    p++;
                }
            }
            else
            {
                // "After any conversion failure, don't bother trying to convert the rest."
                Expand(output, name[p++]);
                tryWide = false;
            }
        }
        return [.. output];
    }

    private void Expand(List<byte> output, byte c)
    {
        if (IsPrint(c, _locale) != 0 && c != '\\')
        {
            output.Add(c);
            return;
        }
        output.Add((byte)'\\');
        string escape = c switch
        {
            0x07 => "a",
            0x08 => "b",
            0x0C => "f",
            0x0A => "n",
            0x0D => "r",
            0x09 => "t",
            0x0B => "v",
            0x5C => "\\",
            _ => Convert.ToString(c, 8).PadLeft(3, '0'),
        };
        output.AddRange(System.Text.Encoding.ASCII.GetBytes(escape));
    }

    /// <summary>
    /// Every character of <paramref name="codePage"/> in every byte form Windows decodes, as one
    /// name unit: single bytes, and lead-trail pairs of a double-byte page. NUL and '/' are left out
    /// (no name holds them). For UTF-8, every BMP character and a sample beyond it.
    /// </summary>
    public static List<byte[]> Units(int codePage)
    {
        var units = new List<byte[]>();
        uint page = (uint)codePage;
        if (codePage == 65001)
        {
            for (int c = 1; c <= 0xFFFF; c++)
            {
                if (c != '/' && c is < 0xD800 or > 0xDFFF)
                    units.Add(System.Text.Encoding.UTF8.GetBytes(((char)c).ToString()));
            }
            for (int c = 0x10000; c <= 0x10FFFF; c += 0x1001)
                units.Add(System.Text.Encoding.UTF8.GetBytes(char.ConvertFromUtf32(c)));
            return units;
        }
        for (int lead = 1; lead <= 0xFF; lead++)
        {
            if (lead == '/')
                continue;
            if (!TarCommandLineEncoding.IsLeadByte(page, (byte)lead))
            {
                if (TarCommandLineEncoding.Decode([(byte)lead], page) is { Length: > 0 })
                    units.Add([(byte)lead]);
                continue;
            }
            for (int trail = 1; trail <= 0xFF; trail++)
            {
                byte[] pair = [(byte)lead, (byte)trail];
                if (TarCommandLineEncoding.Decode(pair, page) is { Length: 1 })
                    units.Add(pair);
            }
        }
        return units;
    }

    /// <summary>
    /// The name libarchive reads from a raw header name (archive_string.c, strncat_in_codepage, the same
    /// in v3.5.2 and v3.8.9): the bytes as they are when the header page is the output page; otherwise
    /// decoded in the header page (MB_ERR_INVALID_CHARS; on failure the raw bytes stay) and encoded in
    /// the output page with best fit and the default character. Measured on PakkoW10 (el-GR, lt-LT,
    /// he-IL, ar-SA): where the default character was used, 3.8.9 skips the entry ("Archive entry has
    /// empty or unreadable filename ... skipping", exit 1) and returns null here when
    /// <paramref name="skipsDefaultCharacter"/>; the in-box 3.5.2 prints the name with it.
    /// </summary>
    public static byte[]? ReadName(byte[] raw, int headerPage, int outputPage, bool skipsDefaultCharacter)
    {
        if (headerPage == outputPage)
            return raw;
        int chars = MultiByteToWideChar((uint)headerPage, MbErrInvalidChars, raw, raw.Length, null, 0);
        if (chars <= 0)
            return raw;
        char[] wide = new char[chars];
        if (MultiByteToWideChar((uint)headerPage, MbErrInvalidChars, raw, raw.Length, wide, chars) != chars)
            return raw;
        int count = WideCharToMultiByte((uint)outputPage, 0, wide, chars, null, 0, IntPtr.Zero, IntPtr.Zero);
        byte[] bytes = new byte[Math.Max(count, 0)];
        int usedDefault = 0;
        int written = outputPage == 65001
            ? WideCharToMultiByte((uint)outputPage, 0, wide, chars, bytes, bytes.Length, IntPtr.Zero, IntPtr.Zero)
            : WideCharToMultiByteUsedDefault((uint)outputPage, 0, wide, chars, bytes, bytes.Length, IntPtr.Zero, out usedDefault);
        if (written != count || count <= 0)
            return raw;
        return usedDefault != 0 && skipsDefaultCharacter ? null : bytes;
    }

    private const uint MbErrInvalidChars = 0x8;

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MultiByteToWideChar(uint codePage, uint flags, [In] byte[] bytes, int count, [Out] char[]? wide, int wideCount);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int WideCharToMultiByte(uint codePage, uint flags, [In] char[] wide, int count, [Out] byte[]? bytes, int byteCount, IntPtr defaultChar, IntPtr usedDefault);

    [LibraryImport("kernel32.dll", EntryPoint = "WideCharToMultiByte", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int WideCharToMultiByteUsedDefault(uint codePage, uint flags, [In] char[] wide, int count, [Out] byte[] bytes, int byteCount, IntPtr defaultChar, out int usedDefault);

    [LibraryImport("ucrtbase.dll", EntryPoint = "_create_locale", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr CreateLocale(int category, string locale);

    [LibraryImport("ucrtbase.dll", EntryPoint = "_free_locale")]
    private static partial void FreeLocale(IntPtr locale);

    [LibraryImport("ucrtbase.dll", EntryPoint = "_mbtowc_l")]
    private static partial int MbToWc(out ushort wide, [In] byte[] bytes, nuint count, IntPtr locale);

    [LibraryImport("ucrtbase.dll", EntryPoint = "_iswprint_l")]
    private static partial int IsWPrint(ushort wide, IntPtr locale);

    [LibraryImport("ucrtbase.dll", EntryPoint = "_isprint_l")]
    private static partial int IsPrint(int c, IntPtr locale);
}
