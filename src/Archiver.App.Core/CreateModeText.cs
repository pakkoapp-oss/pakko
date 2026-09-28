using System.IO.Compression;
using Archiver.Core.Models;
using Archiver.Core.Services;

namespace Archiver.App.Core;

/// <summary>
/// T-F199 step 4: the create-mode words that follow the list and the options. Returns resource
/// keys (plain, read through GetString — T-F104), never text.
/// </summary>
public static class CreateModeText
{
    /// <summary>"Save to" when the list compresses, "Extract to" when it extracts.</summary>
    public static string DestinationLabelKey(PrimaryAction accent) =>
        accent == PrimaryAction.Extract ? "DestinationExtractLabel" : "DestinationSaveLabel";

    /// <summary>
    /// Which items "delete after" sends to the Recycle Bin. The checkbox applies to whichever
    /// button is pressed, so the words follow what is possible, not the accent: the sources after
    /// Compress, the archives after Extract, both named when the list allows both.
    /// </summary>
    public static string DeleteAfterKey(bool canCompress, bool canExtract, bool browsing)
    {
        if (browsing || (canExtract && !canCompress))
            return "DeleteAfterExtractLabel";
        return canExtract ? "DeleteAfterEitherLabel" : "DeleteAfterCompressLabel";
    }

    /// <summary>"ZIP", "TAR.GZ", ... — the extension, as the Compress button and the summary show it.</summary>
    public static string FormatName(ArchiveContainerFormat format) =>
        ArchiveNaming.GetExtension(format)[1..].ToUpperInvariant();

    /// <summary>
    /// The file name Core writes when the name box is blank (T-F264's one rule); for separate
    /// archives, the first item's.
    /// </summary>
    public static string AutoName(IReadOnlyList<string> sources, ArchiveMode mode, ArchiveContainerFormat format)
    {
        IReadOnlyList<string> named = mode == ArchiveMode.SeparateArchives && sources.Count > 1 ? [sources[0]] : sources;
        return ArchiveNaming.ResolveSingleArchiveName(null, named) + ArchiveNaming.GetExtension(format);
    }

    /// <summary>
    /// The parts after the format name in the collapsed "New archive" card: compression (not for
    /// plain tar, which has no level — T-F105) and password state (ZIP only, the one format that
    /// encrypts).
    /// </summary>
    public static IReadOnlyList<string> SummaryKeys(ArchiveContainerFormat format, CompressionLevel level, bool encrypt)
    {
        var keys = new List<string>(2);
        if (format != ArchiveContainerFormat.Tar)
        {
            keys.Add(level switch
            {
                CompressionLevel.Optimal => "SummaryCompressionNormal",
                CompressionLevel.SmallestSize => "SummaryCompressionBest",
                CompressionLevel.NoCompression => "SummaryCompressionNone",
                _ => "SummaryCompressionFast",
            });
        }
        if (format == ArchiveContainerFormat.Zip)
            keys.Add(encrypt ? "SummaryWithPassword" : "SummaryNoPassword");
        return keys;
    }
}
