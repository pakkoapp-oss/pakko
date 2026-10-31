using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Archiver.Core.Text;

/// <summary>
/// T-F441: the one place that asks Windows for this machine's code pages, and the one rule that
/// turns a code page into an <see cref="Encoding"/>. Every text boundary that depends on the machine
/// (tar.exe's command line and output, legacy ZIP names, the CLI console) takes its page from here;
/// <c>TextBoundaryOwnersTests</c> fails when code-page selection appears anywhere else. Values are
/// read on every call, never cached: a caller that needs one page for a whole operation reads it once
/// and keeps it.
/// </summary>
public static partial class SystemCodePages
{
    /// <summary>UTF-8 (the "Beta: Unicode UTF-8" system setting gives it as the ANSI page).</summary>
    public const int Utf8 = 65001;

    private const uint LocaleIDefaultCodePage = 0x0000000B;
    private const uint LocaleIDefaultAnsiCodePage = 0x00001004;
    private const uint LocaleReturnNumber = 0x20000000;

    /// <summary>The system ANSI code page (CP_ACP): command lines of ANSI programs such as tar.exe.</summary>
    public static int Ansi => (int)GetACP();

    /// <summary>The system OEM code page (CP_OEMCP).</summary>
    public static int Oem => (int)GetOEMCP();

    /// <summary>
    /// The ANSI code page of the user's regional format, which a C runtime gets from
    /// <c>setlocale(LC_ALL, "")</c> (libarchive's get_current_codepage); the system ANSI page when the
    /// locale gives none, as libarchive does.
    /// </summary>
    public static int UserAnsi => UserCodePage(LocaleIDefaultAnsiCodePage, Ansi);

    /// <summary>The OEM code page of the user's regional format; the system OEM page when it gives none.</summary>
    public static int UserOem => UserCodePage(LocaleIDefaultCodePage, Oem);

    /// <summary>
    /// The encoding for <paramref name="codePage"/>: UTF-8 without a byte order mark, otherwise the
    /// framework's code-page tables (used directly, never registered process-wide). Unmappable input
    /// becomes the page's replacement character, as Windows' own conversions do.
    /// </summary>
    /// <exception cref="NotSupportedException">The page is not in the framework's tables.</exception>
    public static Encoding For(int codePage) =>
        codePage == Utf8
            ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            : CodePagesEncodingProvider.Instance.GetEncoding(codePage) ?? throw Unknown(codePage);

    /// <summary>As <see cref="For"/>, but a character or byte the page cannot carry throws.</summary>
    /// <exception cref="NotSupportedException">The page is not in the framework's tables.</exception>
    public static Encoding ForStrict(int codePage) =>
        codePage == Utf8
            ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
            : CodePagesEncodingProvider.Instance.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
                ?? throw Unknown(codePage);

    private static NotSupportedException Unknown(int codePage) =>
        new(string.Create(CultureInfo.InvariantCulture, $"Code page {codePage} is not available."));

    private static int UserCodePage(uint type, int fallback)
    {
        int value = 0;
        int written = GetLocaleInfoEx(null, type | LocaleReturnNumber, ref value, sizeof(int) / sizeof(char));
        return written > 0 && value > 0 ? value : fallback;
    }

    [LibraryImport("kernel32.dll")]
    private static partial uint GetACP();

    [LibraryImport("kernel32.dll")]
    private static partial uint GetOEMCP();

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial int GetLocaleInfoEx(string? localeName, uint type, ref int data, int dataCount);
}
