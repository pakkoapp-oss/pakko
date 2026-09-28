using System.Globalization;
using Archiver.Core.Models;
using Archiver.Messages;

namespace Archiver.Shell;

/// <summary>
/// Builds the skipped-list text of an <see cref="ArchiveResult"/> for Archiver.Shell (the
/// classification itself is <see cref="ArchiveResult.Outcome"/>, T-F260).
/// Extracted into a separate class so T-F68 can unit-test the skip-only outcome without
/// launching a process — Program.cs's top-level-statement local functions aren't reachable
/// from Archiver.Shell.Tests, same reason ShellArgumentParser was extracted for T-F57.
/// </summary>
public static class ShellResultPresenter
{
    public static string BuildSkippedMessage(IReadOnlyList<SkippedFile> skipped, int maxLinesShown = 10)
    {
        IEnumerable<string> lines = skipped.Take(maxLinesShown)
            .Select(s => $"{Path.GetFileName(s.Path)}: {MessageText.Render(s, CultureInfo.CurrentUICulture)}");
        string header = ResultMessagesLocalizer.Get("ResultSkippedHeader", skipped.Count);
        string message = $"{header}{Environment.NewLine}{string.Join(Environment.NewLine, lines)}";

        if (skipped.Count > maxLinesShown)
            message += $"{Environment.NewLine}{ResultMessagesLocalizer.Get("ResultAndMoreLine", skipped.Count - maxLinesShown)}";

        return message;
    }
}
