using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Archiver.Core.Text;

/// <summary>
/// T-F441: the one place that asks Windows for this machine's code pages, and the one rule that
/// turns a code page into an <see cref="Encoding"/>. T-F467: read only by the provider,
/// <see cref="MachinePages"/> and <see cref="TextBoundary"/>; every boundary takes its page from a
/// handler of <see cref="TextBoundaries"/> (<c>TextBoundaryOwnersTests</c>). Values are read on every
/// call, never cached: a caller that needs one page for a whole operation keeps one
/// <see cref="MachinePages"/>.
/// </summary>
internal static partial class SystemCodePages
{
    /// <summary>UTF-8 (the "Beta: Unicode UTF-8" system setting gives it as the ANSI page).</summary>
    public const int Utf8 = 65001;

    private const uint LocaleSEnglishLanguageName = 0x00001001;
    private const uint LocaleSEnglishCountryName = 0x00001002;

    /// <summary>The system ANSI code page (CP_ACP): command lines of ANSI programs such as tar.exe.</summary>
    public static int Ansi => (int)GetACP();

    /// <summary>The system OEM code page (CP_OEMCP).</summary>
    public static int Oem => (int)GetOEMCP();

    /// <summary>
    /// T-F467: the name the C runtime gives the user's regional format in <c>setlocale(LC_ALL, "")</c>,
    /// "&lt;English language&gt;_&lt;English country&gt;" (measured against <c>ucrtbase</c> for 12 formats);
    /// null when Windows gives either part empty.
    /// </summary>
    public static string? UcrtLocaleName =>
        LocaleText(LocaleSEnglishLanguageName) is { Length: > 0 } language && LocaleText(LocaleSEnglishCountryName) is { Length: > 0 } country
            ? language + "_" + country
            : null;

    /// <summary>
    /// The encoding for <paramref name="codePage"/>: UTF-8 without a byte order mark, otherwise the
    /// framework's code-page tables (used directly, never registered process-wide), then the pages
    /// built into .NET itself (UTF-16, ASCII, Latin-1), which the tables do not repeat. Unmappable
    /// input becomes the page's replacement character, as Windows' own conversions do.
    /// </summary>
    /// <exception cref="NotSupportedException">The page is in neither.</exception>
    public static Encoding For(int codePage) =>
        codePage == Utf8
            ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            : CodePagesEncodingProvider.Instance.GetEncoding(codePage) ?? BuiltIn(codePage) ?? throw Unknown(codePage);

    /// <summary>As <see cref="For"/>, but a character or byte the page cannot carry throws.</summary>
    /// <exception cref="NotSupportedException">The page is not in the framework's tables.</exception>
    public static Encoding ForStrict(int codePage) =>
        codePage == Utf8
            ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
            : CodePagesEncodingProvider.Instance.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
                ?? BuiltIn(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
                ?? throw Unknown(codePage);

    // Encoding.GetEncoding answers these without a provider; any other page throws there.
    private static readonly HashSet<int> BuiltInCodePages = [1200, 1201, 12000, 12001, 20127, 28591];

    private static Encoding? BuiltIn(int codePage) => BuiltInCodePages.Contains(codePage) ? Encoding.GetEncoding(codePage) : null;

    private static Encoding? BuiltIn(int codePage, EncoderFallback encoderFallback, DecoderFallback decoderFallback) =>
        BuiltInCodePages.Contains(codePage) ? Encoding.GetEncoding(codePage, encoderFallback, decoderFallback) : null;

    private static NotSupportedException Unknown(int codePage) =>
        new(string.Create(CultureInfo.InvariantCulture, $"Code page {codePage} is not available."));

    // The user's regional format's text for type; null when GetLocaleInfoEx fails.
    private static string? LocaleText(uint type)
    {
        int needed = GetLocaleInfoEx(null, type, [], 0);
        if (needed <= 0)
            return null;
        char[] buffer = new char[needed];
        int written = GetLocaleInfoEx(null, type, buffer, buffer.Length);
        return written > 0 && written <= buffer.Length ? new string(buffer, 0, written - 1) : null;
    }

    [LibraryImport("kernel32.dll")]
    private static partial uint GetACP();

    [LibraryImport("kernel32.dll")]
    private static partial uint GetOEMCP();

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial int GetLocaleInfoEx(string? localeName, uint type, [Out] char[] data, int dataCount);
}
