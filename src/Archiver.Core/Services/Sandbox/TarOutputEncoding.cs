using System.Text;
using Archiver.Core.Text;

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
    /// <summary>
    /// The encoding for tar.exe's stdout and stderr on this machine, read now (T-F441). A listing is
    /// decoded twice - by the launcher, then its octal escapes by <see cref="TarNames.ReadListing"/> - so one
    /// <see cref="TarSandboxScope"/> reads it once and uses it for both (<see cref="TarSandboxScope.OutputEncoding"/>).
    /// </summary>
    public static Encoding Current => ForCodePage(SystemCodePages.UserAnsi);

    internal static Encoding ForCodePage(int codePage) => SystemCodePages.For(codePage);
}
