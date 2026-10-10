using System.Globalization;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Messages;

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
    public static OperationMessage? ForArchiveResult(string title, ArchiveResult result)
    {
        OperationMessage? message = result.Outcome switch
        {
            OperationOutcome.Failed => ForErrors(title, result.Errors),
            OperationOutcome.CompletedWithSkips or OperationOutcome.NothingDone => new OperationMessage(
                title, MessageSeverity.Warning, ShellResultPresenter.BuildSkippedMessage(result.SkippedFiles, MaxLinesShown)),
            OperationOutcome.Completed or OperationOutcome.CompletedWithWarnings => null,
            _ => null,
        };

        // T-F280: warnings show whatever the outcome - on their own, or under the errors or skips.
        if (result.Warnings.Count == 0)
            return message;
        string warnings = CappedLines(
            result.Warnings, w => $"{Path.GetFileName(w.SourcePath)}: {MessageText.Render(w, CultureInfo.CurrentUICulture)}");
        return message is null
            ? new OperationMessage(title, MessageSeverity.Warning, warnings)
            : message with { Text = message.Text + Environment.NewLine + Environment.NewLine + warnings };
    }

    private static string CappedLines<T>(IReadOnlyList<T> items, Func<T, string> line)
    {
        string text = string.Join(Environment.NewLine, items.Take(MaxLinesShown).Select(line));
        if (items.Count > MaxLinesShown)
            text += $"{Environment.NewLine}{ResultMessagesLocalizer.Get("ResultAndMoreLine", items.Count - MaxLinesShown)}";
        return text;
    }

    /// <summary>
    /// T-F217: the question before extracting an archive whose declared size is suspiciously large
    /// for its compressed size — the App asks the same (T-F94); declining skips the archive.
    /// </summary>
    public static ConfirmPrompt ForCompressionBomb(CompressionBombWarning warning) => new(
        OperationTextLocalizer.Get("BombTitle"),
        Path.GetFileName(warning.ArchivePath) + Environment.NewLine + Environment.NewLine +
            OperationTextLocalizer.Get("BombMessage",
                ProgressText.FormatBytes(warning.DeclaredUncompressedSize), warning.Ratio.ToString("N0", CultureInfo.CurrentCulture)),
        OperationTextLocalizer.Get("BombExtract"),
        ConflictDialogLocalizer.Get("ConflictDialogSkipButton"));

    // A successful Test leaves nothing on disk, so unlike Extract/Archive it needs its own
    // confirmation, or a silent success would look like nothing happened.
    public static OperationMessage TestPassed(string title) =>
        new(title, MessageSeverity.Information, ResultMessagesLocalizer.Get("ResultNoErrorsDetected"));

    /// <summary>
    /// T-F216: one message for a Test — the skipped list and "no errors" together. T-F274: "no
    /// errors" only when at least one archive was really tested.
    /// </summary>
    public static OperationMessage? ForTestResult(string title, ArchiveResult result)
    {
        OperationMessage? message = result.Outcome == OperationOutcome.Completed ? TestPassed(title) : ForArchiveResult(title, result);
        if (result.Outcome == OperationOutcome.CompletedWithSkips)
            message = message! with { Text = message.Text + Environment.NewLine + Environment.NewLine + TestPassed(title).Text };

        // T-F275 step 3b: a set that matches is said in words, per archive; every other state of a
        // set is already an error or a warning above.
        RecoveryCheck[] matching = [.. result.RecoveryChecks.Where(c => c.Text is not null)];
        if (matching.Length == 0 || message is null)
            return message;
        string lines = CappedLines(
            matching, c => $"{Path.GetFileName(c.ArchivePath)}: {MessageText.Render(c.Text, c.Text!.English, CultureInfo.CurrentUICulture)}");
        return message with { Text = message.Text + Environment.NewLine + Environment.NewLine + lines };
    }

    public static OperationMessage ForHash(string title, HashResult result, IReadOnlyList<string> paths)
    {
        // T-F128 follow-up: a folder result shows only the aggregate Files/Size/DataSum/NamesSum,
        // matching NanaZip's own folder-hash summary; single files keep one line each. Labels are
        // localized; file names and hex hashes are data, not translated.
        // T-F291: under a folder's summary, the entries the sums leave out, named relative to the
        // folder's parent as pakko h names them (T-F221 item 10).
        string[] lines;
        if (result.Folder is { } folder)
        {
            string? folderParent = paths.Count == 1
                ? Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(paths[0])))
                : null;
            HashEntry[] failed = [.. result.Entries.Where(e => e.Error is not null)];
            lines =
            [
                HashResultLocalizer.Get("HashResultFilesLine", folder.FileCount),
                HashResultLocalizer.Get("HashResultSizeLine", ExactSize(folder.TotalBytes)),
                HashResultLocalizer.Get("HashResultDataSumLine", folder.DataSum),
                HashResultLocalizer.Get("HashResultNamesSumLine", folder.NamesSum),
                .. failed.Length > 0 ? [string.Empty] : Array.Empty<string>(),
                .. CappedEntryLines(failed, e => folderParent is null ? e.SourcePath : Path.GetRelativePath(folderParent, e.SourcePath))
            ];
        }
        else
        {
            lines = CappedEntryLines(result.Entries, e => Path.GetFileName(e.SourcePath));
        }

        bool anyErrors = result.Entries.Any(e => e.Error is not null);
        return new OperationMessage(title, anyErrors ? MessageSeverity.Warning : MessageSeverity.Information,
            string.Join(Environment.NewLine, lines), Preformatted: true);
    }

    // Clean copy is deliberately "No threats found in this archive" -- never "safe" -- Pakko doesn't
    // recurse into nested archives and can't make that broader claim (docs/DECISIONS.md, T-F146).
    public static OperationMessage ForScan(string title, ThreatScanResult result, int archiveCount)
    {
        if (result.OverallVerdict == ThreatVerdict.Clean)
            return new OperationMessage(title, MessageSeverity.Information,
                ScanResultLocalizer.Get(archiveCount > 1 ? "ScanNoThreatsFoundMany" : "ScanNoThreatsFound"));

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
                : MessageText.Render(f.ReasonText, f.Reason ?? "unknown", CultureInfo.CurrentUICulture);
            return $"{label}: {detail}";
        }).ToList();

        if (problems.Count > MaxLinesShown)
            lines.Add(ScanResultLocalizer.Get("ScanAndMoreLine", problems.Count - MaxLinesShown));

        bool anyThreat = problems.Any(f => f.Verdict == ThreatVerdict.ThreatDetected);
        return new OperationMessage(title, anyThreat ? MessageSeverity.Error : MessageSeverity.Warning,
            string.Join(Environment.NewLine, lines));
    }

    private static string[] CappedEntryLines(IReadOnlyList<HashEntry> entries, Func<HashEntry, string> name)
    {
        IEnumerable<string> entryLines = entries.Take(MaxLinesShown)
            .Select(e => e.Error is null
                ? $"{name(e)}: {e.Hash}"
                : $"{name(e)}: {MessageText.Render(e.ErrorText, e.Error, CultureInfo.CurrentUICulture)}");
        return entries.Count > MaxLinesShown
            ? [.. entryLines, HashResultLocalizer.Get("HashResultAndMoreLine", entries.Count - MaxLinesShown)]
            : [.. entryLines];
    }

    // "2 KB (2,048 B)": the rounded size plus the exact count in the same unit, which needs no plural.
    private static string ExactSize(long bytes)
    {
        string rounded = ProgressText.FormatBytes(bytes);
        return bytes < 1_024
            ? rounded
            : $"{rounded} ({OperationTextLocalizer.Get("UnitB", bytes.ToString("N0", CultureInfo.CurrentCulture))})";
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

    /// <summary>
    /// T-F235: the Explorer selection did not arrive on stdin. The DLL's own write usually fails
    /// first and says so, but not when the child got a handle that never became its stdin.
    /// </summary>
    public static OperationMessage ForSelectionNotReceived() =>
        new("Pakko", MessageSeverity.Error, ResultMessagesLocalizer.Get("ResultOperationFailed"));

    private static OperationMessage ForErrors(string title, IReadOnlyList<ArchiveError> errors)
    {
        if (errors.Count == 0)
            return new OperationMessage(title, MessageSeverity.Error, ResultMessagesLocalizer.Get("ResultOperationFailed"));

        return new OperationMessage(title, MessageSeverity.Error,
            CappedLines(errors, e => $"{Path.GetFileName(e.SourcePath)}: {MessageText.Render(e, CultureInfo.CurrentUICulture)}"));
    }
}
