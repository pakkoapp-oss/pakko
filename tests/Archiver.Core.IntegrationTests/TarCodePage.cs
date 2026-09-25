using System.Runtime.InteropServices;
using System.Text;

namespace Archiver.Core.IntegrationTests;

// The code pages tar.exe uses on this machine: the system ANSI page for its command line, and
// the user locale's ANSI/OEM pages (libarchive's get_current_codepage/oemcp after
// setlocale(LC_ALL, "")) for the names it prints and for tar headers without a charset.
internal static class TarCodePage
{
    private const uint LocaleIDefaultCodePage = 0x0000000B;
    private const uint LocaleIDefaultAnsiCodePage = 0x00001004;
    private const uint LocaleReturnNumber = 0x20000000;

    public static uint Ansi => GetACP();

    public static bool IsUtf8Ansi => Ansi == 65001;

    public static int UserAnsi => UserCodePage(LocaleIDefaultAnsiCodePage, (int)GetACP());

    public static int UserOem => UserCodePage(LocaleIDefaultCodePage, (int)GetOEMCP());

    // A name tar.exe can show on this machine and that a legacy tool could have written in its
    // OEM page — Cyrillic on a uk/ru machine, Latin-1 on an en-US one.
    public static string? PortableNonAsciiName()
    {
        foreach (string candidate in new[] { "Док.txt", "café.txt" })
        {
            if (RoundTrips(candidate, UserAnsi) && RoundTrips(candidate, UserOem) && RoundTrips(candidate, (int)Ansi))
                return candidate;
        }
        return null;
    }

    public static bool RoundTrips(string value, int codePage)
    {
        if (codePage == 65001)
            return true;
        Encoding encoding = Encoding(codePage);
        try { return encoding.GetString(encoding.GetBytes(value)) == value; }
        catch (EncoderFallbackException) { return false; }
    }

    public static Encoding Encoding(int codePage) =>
        codePage == 65001
            ? new UTF8Encoding(false)
            : CodePagesEncodingProvider.Instance.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)!;

    private static int UserCodePage(uint type, int fallback)
    {
        int value = 0;
        int written = GetLocaleInfoEx(null, type | LocaleReturnNumber, ref value, sizeof(int) / sizeof(char));
        return written > 0 && value > 0 ? value : fallback;
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetACP();

    [DllImport("kernel32.dll")]
    private static extern uint GetOEMCP();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetLocaleInfoEx(string? localeName, uint type, ref int data, int dataCount);
}
