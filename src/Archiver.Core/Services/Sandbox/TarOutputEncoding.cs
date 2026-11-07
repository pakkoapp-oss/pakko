using System.Text;
using Archiver.Core.Text;

namespace Archiver.Core.Services.Sandbox;

/// <summary>
/// T-F244 item 2 / T-F204: the encoding tar.exe writes names and messages in. bsdtar calls
/// setlocale(LC_ALL, "") and libarchive converts with that locale's code page
/// (get_current_codepage). T-F465: the UCRT names that locale "&lt;regional format&gt;.&lt;system ANSI page&gt;", so
/// the page is the system ANSI page, the one its command line goes through
/// (<see cref="TarCommandLineEncoding"/>), not the regional format's (measured 2026-11-07 on 3.8.9
/// with formats en-US and ja-JP over system 1251, and on 3.5.2 with uk-UA over 1252). Reading it in
/// the format's page showed one entry under another's name, and a selection took that other entry.
/// Reading it as UTF-8 turned every non-ASCII name into U+FFFD (T-F244).
/// </summary>
internal static class TarOutputEncoding
{
    /// <summary>
    /// The encoding for tar.exe's stdout and stderr on this machine, read now (T-F441). A listing is
    /// decoded twice - by the launcher, then its octal escapes by <see cref="TarNames.ReadListing"/> - so one
    /// <see cref="TarSandboxScope"/> reads it once and uses it for both (<see cref="TarSandboxScope.OutputEncoding"/>).
    /// </summary>
    public static Encoding Current => ForCodePage((int)TarCommandLineEncoding.AnsiCodePage);

    internal static Encoding ForCodePage(int codePage) => SystemCodePages.For(codePage);
}
