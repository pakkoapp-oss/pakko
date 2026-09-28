using Archiver.Core.Models;

namespace Archiver.App.Core;

/// <summary>
/// T-F211: the result line the footer keeps until the next action (it used to be overwritten by
/// "Ready" a moment after the operation ended, so a clean run showed nothing).
/// </summary>
/// <param name="Outcome">Core's classification of the whole run.</param>
/// <param name="Extract">True for an extraction, false for compression.</param>
/// <param name="CreatedCount">Archives created (compress), or result folders — one per extracted
/// archive (extract).</param>
/// <param name="ProblemCount">Errors plus skipped items.</param>
/// <param name="Seconds">Elapsed whole seconds, at least 1 so the line never says "0 s".</param>
/// <param name="ShowInFolderPath">What "Show in folder" points at, or null when nothing was created.</param>
/// <param name="DestinationFolder">The folder the operation wrote into.</param>
public sealed record OutcomeLine(
    OperationOutcome Outcome, bool Extract, int CreatedCount, int ProblemCount, int Seconds, string? ShowInFolderPath, string DestinationFolder)
{
    /// <summary>True when "Details..." has something to show.</summary>
    public bool HasDetails => ProblemCount > 0;

    /// <summary>The resource key of the line (plain, read through GetString — T-F104).</summary>
    public string TextKey => Outcome switch
    {
        OperationOutcome.Completed => Extract ? "OutcomeExtracted" : "OutcomeCompressed",
        OperationOutcome.NothingDone => "OutcomeNothingDone",
        _ => "OutcomeProblems",
    };

    /// <summary>The line's format arguments: seconds and count for a clean run, else the problem count.</summary>
    public IReadOnlyList<int> TextArgs =>
        Outcome == OperationOutcome.Completed ? [Seconds, CreatedCount] : [ProblemCount];

    /// <summary>
    /// Explorer's arguments for "Show in folder": the created item selected in its folder, or the
    /// destination opened when the extraction landed straight in it (a flat archive reports the
    /// destination itself). Null when nothing was created.
    /// </summary>
    public string? ExplorerArguments
    {
        get
        {
            if (ShowInFolderPath is null)
                return null;
            return string.Equals(Normalize(ShowInFolderPath), Normalize(DestinationFolder), StringComparison.OrdinalIgnoreCase)
                ? $"\"{ShowInFolderPath}\""
                : $"/select,\"{ShowInFolderPath}\"";
        }
    }

    /// <summary>Builds the line from an operation's result.</summary>
    public static OutcomeLine From(ArchiveResult result, TimeSpan elapsed, bool extract, string destinationFolder) => new(
        result.Outcome,
        extract,
        result.CreatedFiles.Count,
        result.Errors.Count + result.SkippedFiles.Count,
        Math.Max(1, (int)Math.Round(elapsed.TotalSeconds)),
        result.CreatedFiles.Count > 0 ? result.CreatedFiles[0] : null,
        destinationFolder);

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(path);
}

/// <summary>What the footer's first line shows (T-F199 step 7).</summary>
public enum FooterLineKind
{
    /// <summary>Nothing: an operation is running (the status line and progress speak), or nothing to say.</summary>
    None,

    /// <summary>The last operation's result, kept until the next action.</summary>
    Outcome,

    /// <summary>Browse mode: "2 of 12 selected".</summary>
    Selection,

    /// <summary>Create mode: what the primary button will do with the list.</summary>
    Preview,
}

/// <summary>The footer's first-line precedence: busy, then the outcome, then browse selection, then the preview.</summary>
public static class FooterLine
{
    /// <summary>Picks what the footer's first line shows.</summary>
    public static FooterLineKind Pick(bool busy, bool hasOutcome, bool browsing, int selectedCount, bool listHasItems)
    {
        if (busy)
            return FooterLineKind.None;
        if (hasOutcome)
            return FooterLineKind.Outcome;
        if (browsing)
            return selectedCount > 0 ? FooterLineKind.Selection : FooterLineKind.None;
        return listHasItems ? FooterLineKind.Preview : FooterLineKind.None;
    }
}
