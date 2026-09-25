using System.Runtime.InteropServices;
using System.Text;

namespace Archiver.Core.Services.Zip;

/// <summary>
/// T-F234: the two legacy code pages a ZIP entry name without the UTF-8 flag can be in — OEM for
/// names written on FAT/NTFS hosts, ANSI for other hosts (7-Zip's rule, see
/// <see cref="ZipEntryNameDecoder"/>). <see cref="System"/> reads this machine's pages; tests pass
/// explicit ones so expectations don't depend on the machine.
/// </summary>
internal sealed record ZipNameCodePages(Encoding Oem, Encoding Ansi)
{
    private const int Utf8CodePage = 65001;

    /// <summary>This machine's OEM and ANSI code pages (what 7-Zip's CP_OEMCP/CP_ACP resolve to).</summary>
    public static ZipNameCodePages System { get; } = FromCodePages((int)GetOEMCP(), (int)GetACP());

    public static ZipNameCodePages FromCodePages(int oem, int ansi) => new(ForCodePage(oem), ForCodePage(ansi));

    // CodePagesEncodingProvider ships in the shared framework (no NuGet) — used directly rather
    // than via Encoding.RegisterProvider, which would change process-wide state.
    private static Encoding ForCodePage(int codePage) =>
        codePage == Utf8CodePage
            ? Encoding.UTF8
            : CodePagesEncodingProvider.Instance.GetEncoding(codePage) ?? Encoding.Latin1;

    [DllImport("kernel32.dll")]
    private static extern uint GetOEMCP();

    [DllImport("kernel32.dll")]
    private static extern uint GetACP();
}
