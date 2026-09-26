using Archiver.Core.Models;
using Archiver.Core.Services;

namespace Archiver.Shell;

/// <summary>
/// Builds the result message each Explorer command shows (T-F268) — pure, so the text is
/// unit-testable without a dialog. Only what went wrong is listed, at most
/// <see cref="MaxLinesShown"/> lines plus an "and N more" line.
/// </summary>
internal static class OperationMessages
{
    public const int MaxLinesShown = 10;

    /// <summary>Null for a clean success: Extract/Archive leave their result on disk.</summary>
    public static OperationMessage? ForArchiveResult(string title, ArchiveResult result) =>
        ShellResultPresenter.Classify(result) switch
        {
            ShellResultOutcome.Failed => ForErrors(title, result.Errors),
            ShellResultOutcome.SkippedOnly => new OperationMessage(
                title, MessageSeverity.Warning, ShellResultPresenter.BuildSkippedMessage(result.SkippedFiles, MaxLinesShown)),
            _ => null,
        };

    // A successful Test leaves nothing on disk, so unlike Extract/Archive it needs its own
    // confirmation, or a silent success would look like nothing happened.
    public static OperationMessage TestPassed(string title) =>
        new(title, MessageSeverity.Information, ResultMessagesLocalizer.Get("ResultNoErrorsDetected"));

    public static OperationMessage ForHash(string title, HashResult result)
    {
        // T-F128 follow-up: a folder result shows only the aggregate Files/Size/DataSum/NamesSum,
        // matching NanaZip's own folder-hash summary; single files keep one line each. Labels are
        // localized; file names and hex hashes are data, not translated.
        string[] lines;
        if (result.Folder is { } folder)
        {
            lines =
            [
                HashResultLocalizer.Get("HashResultFilesLine", folder.FileCount),
                HashResultLocalizer.Get("HashResultSizeLine", $"{ProgressText.FormatBytes(folder.TotalBytes)} ({folder.TotalBytes:N0} bytes)"),
                HashResultLocalizer.Get("HashResultDataSumLine", folder.DataSum),
                HashResultLocalizer.Get("HashResultNamesSumLine", folder.NamesSum)
            ];
        }
        else
        {
            var entryLines = result.Entries.Take(MaxLinesShown)
                .Select(e => e.Error is null
                    ? $"{Path.GetFileName(e.SourcePath)}: {e.Hash}"
                    : $"{Path.GetFileName(e.SourcePath)}: {e.Error}");
            lines = result.Entries.Count > MaxLinesShown
                ? [.. entryLines, HashResultLocalizer.Get("HashResultAndMoreLine", result.Entries.Count - MaxLinesShown)]
                : [.. entryLines];
        }

        bool anyErrors = result.Entries.Any(e => e.Error is not null);
        return new OperationMessage(title, anyErrors ? MessageSeverity.Warning : MessageSeverity.Information,
            string.Join(Environment.NewLine, lines));
    }

    // Clean copy is deliberately "No threats found in this archive" -- never "safe" -- Pakko doesn't
    // recurse into nested archives and can't make that broader claim (docs/DECISIONS.md, T-F146).
    public static OperationMessage ForScan(string title, ThreatScanResult result)
    {
        if (result.OverallVerdict == ThreatVerdict.Clean)
            return new OperationMessage(title, MessageSeverity.Information, ScanResultLocalizer.Get("ScanNoThreatsFound"));

        var problems = result.Findings.Where(f => f.Verdict != ThreatVerdict.Clean).ToList();
        var lines = problems.Take(MaxLinesShown).Select(f =>
        {
            string label = f.EntryPath is { } entry
                ? $"{Path.GetFileName(f.ArchivePath)}/{entry}"
                : Path.GetFileName(f.ArchivePath);
            // AMSI never returns a threat name, so the generic phrase is what ships. A provider name
            // is shown only if a future provider supplies one. The scan service always gives an
            // Inconclusive finding a reason, so the "unknown" fallback is defensive only.
            string detail = f.Verdict == ThreatVerdict.ThreatDetected
                ? f.ThreatName ?? ScanResultLocalizer.Get("ScanThreatDetectedGeneric")
                : f.Reason ?? "unknown";
            return $"{label}: {detail}";
        }).ToList();

        if (problems.Count > MaxLinesShown)
            lines.Add(ScanResultLocalizer.Get("ScanAndMoreLine", problems.Count - MaxLinesShown));

        bool anyThreat = problems.Any(f => f.Verdict == ThreatVerdict.ThreatDetected);
        return new OperationMessage(title, anyThreat ? MessageSeverity.Error : MessageSeverity.Warning,
            string.Join(Environment.NewLine, lines));
    }

    /// <summary>Null when Archiver.App was opened; the App takes over from there.</summary>
    public static OperationMessage? ForLaunch(AppLaunchResult result)
    {
        string? key = result switch
        {
            AppLaunchResult.TooManyFiles => "OpenUiTooManyFiles",
            AppLaunchResult.NoPackage => "OpenUiNoPackage",
            AppLaunchResult.Failed => "ResultOperationFailed",
            _ => null,
        };
        return key is null ? null : new OperationMessage("Pakko", MessageSeverity.Error, ResultMessagesLocalizer.Get(key));
    }

    private static OperationMessage ForErrors(string title, IReadOnlyList<ArchiveError> errors)
    {
        if (errors.Count == 0)
            return new OperationMessage(title, MessageSeverity.Error, ResultMessagesLocalizer.Get("ResultOperationFailed"));

        var text = string.Join(Environment.NewLine,
            errors.Take(MaxLinesShown).Select(e => $"{Path.GetFileName(e.SourcePath)}: {e.Message}"));
        if (errors.Count > MaxLinesShown)
            text += $"{Environment.NewLine}{ResultMessagesLocalizer.Get("ResultAndMoreLine", errors.Count - MaxLinesShown)}";

        return new OperationMessage(title, MessageSeverity.Error, text);
    }
}
