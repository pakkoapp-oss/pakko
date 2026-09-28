namespace Archiver.App.Core;

/// <summary>The two actions the main window offers for its list.</summary>
public enum PrimaryAction
{
    /// <summary>Create an archive from the list.</summary>
    Compress,

    /// <summary>Extract the archives in the list.</summary>
    Extract,
}

/// <summary>What the list allows, and which action the window emphasizes.</summary>
/// <param name="CanCompress">The list is not empty.</param>
/// <param name="CanExtract">At least one item is an archive Pakko may open (T-F212).</param>
/// <param name="ExtractUnavailable">The list has items but none is such an archive — the window says why Extract is off.</param>
/// <param name="Accent">The button drawn as the primary one.</param>
/// <param name="ArchivesOnly">Every item is such an archive: the "New archive" options collapse.</param>
/// <param name="ExtractablePaths">The items Extract runs on, in list order.</param>
public sealed record ListActions(
    bool CanCompress,
    bool CanExtract,
    bool ExtractUnavailable,
    PrimaryAction Accent,
    bool ArchivesOnly,
    IReadOnlyList<string> ExtractablePaths);

/// <summary>
/// T-F199 (option A, user decision 2026-09-27): two action buttons, the accent on the one that fits
/// the list — archives only: Extract, anything else: Compress. T-F212: Extract runs only on items
/// that are archives Pakko may open; <c>canOpen</c> is Core's
/// <c>ArchiveFormatPolicy.CanOpenByExtension</c> (policy, tar.exe, no disk I/O).
/// </summary>
public static class PrimaryActionPolicy
{
    /// <summary>Evaluates the list.</summary>
    public static ListActions Evaluate(IReadOnlyList<(string Path, bool IsFolder)> items, Func<string, bool> canOpen)
    {
        string[] extractable = [.. items.Where(i => !i.IsFolder && canOpen(i.Path)).Select(i => i.Path)];
        bool archivesOnly = items.Count > 0 && extractable.Length == items.Count;
        return new ListActions(
            CanCompress: items.Count > 0,
            CanExtract: extractable.Length > 0,
            ExtractUnavailable: items.Count > 0 && extractable.Length == 0,
            Accent: archivesOnly ? PrimaryAction.Extract : PrimaryAction.Compress,
            ArchivesOnly: archivesOnly,
            ExtractablePaths: extractable);
    }
}
