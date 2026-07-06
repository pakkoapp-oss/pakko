using Archiver.Core.Models;

namespace Archiver.Shell;

/// <summary>Outcome of a shell-triggered archive/extract operation, for dialog selection.</summary>
public enum ShellResultOutcome
{
    Success,
    SkippedOnly,
    Failed,
}

/// <summary>
/// Classifies an <see cref="ArchiveResult"/> and builds dialog text for Archiver.Shell.
/// Extracted into a separate class so T-F68 can unit-test the skip-only outcome without
/// launching a process — Program.cs's top-level-statement local functions aren't reachable
/// from Archiver.Shell.Tests, same reason ShellArgumentParser was extracted for T-F57.
/// </summary>
public static class ShellResultPresenter
{
    /// <summary>
    /// Errors win over skips: a run with any error keeps the existing "operation failed" dialog.
    /// A skip-only run (Success and no errors, but entries were rejected by a validation gate)
    /// gets its own dialog instead of completing silently.
    /// </summary>
    public static ShellResultOutcome Classify(ArchiveResult result)
    {
        if (!result.Success || result.Errors.Count > 0)
            return ShellResultOutcome.Failed;

        return result.SkippedFiles.Count > 0
            ? ShellResultOutcome.SkippedOnly
            : ShellResultOutcome.Success;
    }

    public static string BuildSkippedMessage(IReadOnlyList<SkippedFile> skipped, int maxLinesShown = 10)
    {
        var noun = skipped.Count == 1 ? "entry" : "entries";
        var lines = skipped.Take(maxLinesShown)
            .Select(s => $"{Path.GetFileName(s.Path)}: {s.Reason}");
        var message = $"{skipped.Count} {noun} skipped:{Environment.NewLine}{string.Join(Environment.NewLine, lines)}";

        if (skipped.Count > maxLinesShown)
            message += $"{Environment.NewLine}…and {skipped.Count - maxLinesShown} more";

        return message;
    }
}
