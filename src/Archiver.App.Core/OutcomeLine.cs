using Archiver.Core.Models;

namespace Archiver.App.Core;

/// <summary>
/// T-F211: the result line the footer keeps until the next action (it used to be overwritten by
/// "Ready" a moment after the operation ended, so a clean run showed nothing).
/// </summary>
/// <param name="Outcome">Core's classification of the whole run.</param>
/// <param name="CreatedCount">Archives created (compress) or archives extracted (extract).</param>
/// <param name="ProblemCount">Errors plus skipped items.</param>
/// <param name="Seconds">Elapsed whole seconds, at least 1 so the line never says "0 s".</param>
/// <param name="ShowInFolderPath">What "Show in folder" opens, or null when nothing was created.</param>
public sealed record OutcomeLine(OperationOutcome Outcome, int CreatedCount, int ProblemCount, int Seconds, string? ShowInFolderPath)
{
    /// <summary>True when "Details..." has something to show.</summary>
    public bool HasDetails => ProblemCount > 0;

    /// <summary>Builds the line from an operation's result.</summary>
    public static OutcomeLine From(ArchiveResult result, TimeSpan elapsed) => new(
        result.Outcome,
        result.CreatedFiles.Count,
        result.Errors.Count + result.SkippedFiles.Count,
        Math.Max(1, (int)Math.Round(elapsed.TotalSeconds)),
        result.CreatedFiles.Count > 0 ? result.CreatedFiles[0] : null);
}
