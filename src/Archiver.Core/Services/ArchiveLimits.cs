using System.Globalization;
using Archiver.Core.Models;

namespace Archiver.Core.Services;

/// <summary>
/// T-F513: how many entries Pakko reads from one archive. An archive's catalog is held whole while
/// it is listed, tested or extracted (.NET's <c>ZipArchive</c> alone keeps about 455 bytes an entry),
/// so an archive declaring millions of entries could take any amount of memory; past this limit it
/// is refused with its own message, before its catalog is read. Measured 2026-11-14: about 1 KB an
/// entry for a ZIP, 600 bytes for a tar (docs/DECISIONS.md, "T-F513").
/// </summary>
internal static class ArchiveLimits
{
    /// <summary>The most entries one archive may have.</summary>
    public const int MaxEntries = 2_000_000;

    /// <summary>The most bytes a tar.exe listing may write: the archive helper's stdout cap.</summary>
    public const long MaxListingBytes = 1L << 30;

    /// <summary>The refusal, as an exception every catch of a damaged archive already handles.</summary>
    public static CoreTextIOException TooManyEntries(int limit) =>
        new(CoreMessages.Text(MessageCode.TooManyEntries, limit.ToString("N0", CultureInfo.CurrentCulture)));
}
