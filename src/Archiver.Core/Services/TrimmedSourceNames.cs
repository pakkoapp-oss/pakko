using Archiver.Core.Models;

namespace Archiver.Core.Services;

/// <summary>
/// T-F509: the sources of a creation whose name Windows trims ("a.txt ", "d."). By its plain path
/// such a source opened a twin beside it ("a.txt"), whose bytes went in under the source's name, and
/// the run counted the source done, so deleting the sources afterwards took the twin. Each one is
/// refused before the engine sees the list - before <see cref="SourcePathNormalizer"/>, whose
/// <see cref="Path.GetFullPath(string)"/> trims, and before the entry names are claimed. The
/// <c>\\?\</c> form is refused too: tar.exe cannot read such a name, and one rule keeps the frontends'
/// deletion keys simple (T-F510 archives them through ZIP).
/// </summary>
internal static class TrimmedSourceNames
{
    /// <summary>The options with those sources left out, and the result for them; null when none.</summary>
    internal static (ArchiveOptions Kept, ArchiveResult? Refused) Split(ArchiveOptions options)
    {
        var keptPaths = new List<string>(options.SourcePaths.Count);
        List<int>? keptParts = options.SourceNameParts is null ? null : [];
        var errors = new List<ArchiveError>();
        var sources = new List<SourceResult>();
        for (int i = 0; i < options.SourcePaths.Count; i++)
        {
            string path = options.SourcePaths[i];
            if (InputPaths.Problem(path) is { Code: MessageCode.SourceNameTrimmed } trimmed)
            {
                errors.Add(CoreMessages.Error(path, trimmed));
                sources.Add(new SourceResult { Path = path, Outcome = SourceOutcome.NotProcessed });
                continue;
            }
            keptPaths.Add(path);
            if (keptParts is not null && i < options.SourceNameParts!.Count)
                keptParts.Add(options.SourceNameParts[i]);
        }
        if (errors.Count == 0)
            return (options, null);
        // The default name counts every source asked for: two sources name the archive after their folder.
        string? archiveName = options.ArchiveName ?? (options.Mode == ArchiveMode.SingleArchive && options.ExactFileName is null
            ? ArchiveNaming.GetDefaultArchiveName(options.SourcePaths)
            : null);
        // The run is not a success, so its folder is not opened as if it were.
        return (options with { SourcePaths = keptPaths, SourceNameParts = keptParts, ArchiveName = archiveName, OpenDestinationFolder = false },
            new ArchiveResult { Errors = errors, Sources = sources });
    }

    /// <summary>The engine's result for the kept sources, with the refused ones added.</summary>
    internal static ArchiveResult Merge(ArchiveResult refused, ArchiveResult kept) => kept with
    {
        Errors = [.. refused.Errors, .. kept.Errors],
        Sources = [.. refused.Sources, .. kept.Sources],
    };
}
