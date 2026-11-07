using System.Text;
using Archiver.Core.Text;

namespace Archiver.Core.Services.Zip;

/// <summary>
/// T-F234: the two legacy code pages a ZIP entry name without the UTF-8 flag can be in — OEM for
/// names written on FAT/NTFS hosts, ANSI for other hosts (7-Zip's rule, see
/// <see cref="ZipEntryNameDecoder"/>). <see cref="Current"/> reads this machine's pages; tests pass
/// explicit ones so expectations don't depend on the machine.
/// </summary>
internal sealed record ZipNameCodePages(Encoding Oem, Encoding Ansi)
{
    /// <summary>This machine's OEM and ANSI code pages now (what 7-Zip's CP_OEMCP/CP_ACP resolve to; T-F467's handler).</summary>
    public static ZipNameCodePages Current()
    {
        (TextBoundary oem, TextBoundary ansi) = TextBoundaries.ZipLegacyNames(MachinePages.Current);
        return new(oem.Lenient, ansi.Lenient);
    }

    public static ZipNameCodePages FromCodePages(int oem, int ansi) => new(new TextBoundary(oem).Lenient, new TextBoundary(ansi).Lenient);
}
