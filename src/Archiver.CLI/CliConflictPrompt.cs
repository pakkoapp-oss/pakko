using System.Globalization;
using Archiver.Core.Models;
using Archiver.Core.Services;

namespace Archiver.CLI;

/// <summary>
/// T-F160: `pakko x`'s interactive overwrite prompt, modeled on real 7-Zip's console — NanaZip's
/// vendored UI/Console/ExtractCallbackConsole.cpp (AskOverwrite) and UserInputUtils.cpp
/// (ScanUserYesNoAllQuit), fetched while designing this: a text prompt, not a dialog, re-asked
/// until one valid letter, with end-of-input treated as quit. Written to stderr rather than 7z's
/// stdout, because `-so` streams data on stdout and the T-F191 password prompt already uses
/// stderr. Reads through an injected line source so the logic is unit-testable — the Subprocess/
/// test layer always redirects stdin, so the real prompt is unreachable end to end.
/// </summary>
public static class CliConflictPrompt
{
    private const string Question = "? (Y)es / (N)o / (A)lways / (S)kip all / A(u)to rename all / (Q)uit? ";

    /// <summary>Asks once (re-asking on invalid input). Returns null for Quit or end of input.</summary>
    public static ConflictDecision? Ask(ConflictInfo conflict, Func<string?> readLine, Action<string> write)
    {
        // T-F295: both files' size and time, as 7-Zip's AskOverwrite shows them.
        var existing = new FileInfo(conflict.ExistingPath);
        string nl = Environment.NewLine;
        write(nl + "Would you like to replace the existing file:" + nl
              + "  " + conflict.ExistingPath + nl
              + (existing.Exists ? Details(existing.Length, existing.LastWriteTime) : "")
              + "with the file from the archive?" + nl
              + Details(conflict.IncomingSize, conflict.IncomingModified?.LocalDateTime));

        while (true)
        {
            write(Question);
            string? line = readLine();
            if (line is null)
                return null;

            string answer = line.Trim();
            if (answer.Length != 1)
                continue;

            switch (char.ToLowerInvariant(answer[0]))
            {
                case 'y': return new ConflictDecision { Resolution = ConflictResolution.Overwrite };
                case 'n': return new ConflictDecision { Resolution = ConflictResolution.Skip };
                case 'a': return new ConflictDecision { Resolution = ConflictResolution.Overwrite, ApplyToAll = true };
                case 's': return new ConflictDecision { Resolution = ConflictResolution.Skip, ApplyToAll = true };
                case 'u': return new ConflictDecision { Resolution = ConflictResolution.Rename, ApplyToAll = true };
                case 'q': return null;
            }
        }
    }

    private static string Details(long? size, DateTime? modified) =>
        (size is { } bytes ? "  Size:     " + bytes.ToString("N0", CultureInfo.InvariantCulture) + " bytes" + Environment.NewLine : "")
        + (modified is { } time ? "  Modified: " + time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + Environment.NewLine : "");
    /// <summary>
    /// The resolver `pakko x` wires into ExtractOptions.ResolveConflictAsync: one sticky instance per
    /// command, so "Always"/"Skip all"/"Auto rename all" also span ExtractionRouter's separate zip
    /// and tar Core calls. Quit (or end of input) cancels <paramref name="quit"/> and declines the
    /// current file; Core then stops at its next cancellation check and cleans up its temp output.
    /// </summary>
    public static StickyCallback<ConflictInfo, ConflictDecision> CreateResolver(
        Func<string?> readLine, Action<string> write, CancellationTokenSource quit) =>
        new(conflict =>
        {
            ConflictDecision? decision = Ask(conflict, readLine, write);
            if (decision is not null)
                return Task.FromResult(decision);

            quit.Cancel();
            return Task.FromResult(new ConflictDecision { Resolution = ConflictResolution.Skip, ApplyToAll = true });
        }, d => d.ApplyToAll);
}
