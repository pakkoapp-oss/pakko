using System.Runtime.InteropServices;
using System.Text;

namespace Archiver.Core.Services.Sandbox;

/// <summary>
/// T-F244 item 2 / T-F204: the encoding tar.exe writes names and messages in. bsdtar calls
/// setlocale(LC_ALL, "") and libarchive converts with that locale's code page
/// (get_current_codepage) — the ANSI code page of the user's regional format, not UTF-8, and not
/// necessarily the system ANSI code page its command line goes through (<see cref="TarCommandLineEncoding"/>).
/// Confirmed 2026-09-25 on uk-UA (both 1251): a 7z entry "Док" is printed as C4 EE EA. Reading
/// that output as UTF-8 turned every non-ASCII name in a listing into U+FFFD.
/// </summary>
internal static class TarOutputEncoding
{
    private const uint LocaleIDefaultAnsiCodePage = 0x00001004;
    private const uint LocaleReturnNumber = 0x20000000;
    private const int CpUtf8 = 65001;

    /// <summary>The encoding for tar.exe's stdout and stderr on this machine.</summary>
    public static Encoding Current { get; } = ForCodePage(UserAnsiCodePage());

    // CodePagesEncodingProvider ships in the shared framework (no NuGet) and is used directly,
    // not registered process-wide — same as ZipNameCodePages.
    internal static Encoding ForCodePage(int codePage) =>
        codePage == CpUtf8
            ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            : CodePagesEncodingProvider.Instance.GetEncoding(codePage) ?? Encoding.Latin1;

    // libarchive falls back to GetACP() when the locale gives no code page; so does this.
    private static int UserAnsiCodePage()
    {
        int value = 0;
        int written = GetLocaleInfoEx(null, LocaleIDefaultAnsiCodePage | LocaleReturnNumber, ref value, sizeof(int) / sizeof(char));
        return written > 0 && value > 0 ? value : (int)GetACP();
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetLocaleInfoEx(string? localeName, uint type, ref int data, int dataCount);

    [DllImport("kernel32.dll")]
    private static extern uint GetACP();
}
