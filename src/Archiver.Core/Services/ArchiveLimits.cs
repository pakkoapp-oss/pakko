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

    /// <summary>
    /// T-F529: the most characters one archive's names may have together. A list, a test, a scan
    /// hold every name (the list as text, T-F234's collision check as a set), so names of up to
    /// 65,535 bytes each could fill any memory within the entry limit. Measured 2026-11-16 at
    /// 4,096 names of 32,000 characters (131 million): a ZIP list peaks at 413 MiB, a test at 570, a
    /// tar list through tar.exe at 1,140. A ZIP and the helper count the decoded names; tar.exe's
    /// listing is counted in bytes, never fewer than its characters.
    /// </summary>
    public const int MaxCatalogNameChars = 128 * 1024 * 1024;

    /// <summary>The refusal, as an exception every catch of a damaged archive already handles.</summary>
    public static CoreTextIOException TooManyEntries(int limit) =>
        new(CoreMessages.Text(MessageCode.TooManyEntries, limit.ToString("N0", CultureInfo.CurrentCulture)));

    /// <summary>T-F529: the refusal of names too long together, handled as <see cref="TooManyEntries"/> is.</summary>
    public static CoreTextIOException CatalogTooLarge(long limit) =>
        new(CoreMessages.Text(MessageCode.CatalogTooLarge, limit.ToString("N0", CultureInfo.CurrentCulture)));

    /// <summary>T-F529: one name's characters counted against <paramref name="limit"/>; past it, the refusal.</summary>
    public static long CountName(long counted, int nameChars, long limit) =>
        counted + nameChars <= limit ? counted + nameChars : throw CatalogTooLarge(limit);
}
