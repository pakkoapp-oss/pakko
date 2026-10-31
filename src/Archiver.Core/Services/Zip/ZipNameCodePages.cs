using System.Text;
using Archiver.Core.Text;

namespace Archiver.Core.Services.Zip;

/// <summary>
/// T-F234: the two legacy code pages a ZIP entry name without the UTF-8 flag can be in — OEM for
/// names written on FAT/NTFS hosts, ANSI for other hosts (7-Zip's rule, see
/// <see cref="ZipEntryNameDecoder"/>). <see cref="System"/> reads this machine's pages; tests pass
/// explicit ones so expectations don't depend on the machine.
/// </summary>
internal sealed record ZipNameCodePages(Encoding Oem, Encoding Ansi)
{
    /// <summary>This machine's OEM and ANSI code pages (what 7-Zip's CP_OEMCP/CP_ACP resolve to).</summary>
    public static ZipNameCodePages System { get; } = FromCodePages(SystemCodePages.Oem, SystemCodePages.Ansi);

    public static ZipNameCodePages FromCodePages(int oem, int ansi) => new(SystemCodePages.For(oem), SystemCodePages.For(ansi));
}
