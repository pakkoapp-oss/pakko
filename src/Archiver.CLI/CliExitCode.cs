using Archiver.Core.Models;

namespace Archiver.CLI;

/// <summary>
/// The exit-code table over a finished <see cref="ArchiveResult"/>: 0 clean success, 1 success
/// with warnings or skips, 2 the operation failed.
/// </summary>
public static class CliExitCode
{
    /// <summary>The exit code of <paramref name="result"/>.</summary>
    /// <param name="result">What the operation did.</param>
    /// <param name="keptExistingByChoice">-aos was given (T-F415).</param>
    public static int For(ArchiveResult result, bool keptExistingByChoice)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (keptExistingByChoice)
            result = WithoutKeptExisting(result);
        return result.Outcome switch
        {
            OperationOutcome.Completed => result.KeptExistingFiles.Count > 0 ? 1 : 0,
            OperationOutcome.Failed => 2,
            OperationOutcome.CompletedWithWarnings or OperationOutcome.CompletedWithSkips or OperationOutcome.NothingDone => 1,
            _ => 1,
        };
    }

    // -aos asked for exactly these: the files kept because they exist (ZIP names them apart, the
    // tar engine as skips of the same code), and ZIP's "every entry was skipped" when nothing but
    // kept files explains it. A skip for any other reason stays, an entry lost to an earlier one of
    // this run (FileWrittenEarlierInThisRun) too: no file that was there was kept for it.
    private static ArchiveResult WithoutKeptExisting(ArchiveResult result)
    {
        bool anyKept = result.KeptExistingFiles.Any(IsKeptExisting) || result.SkippedFiles.Any(IsKeptExisting);
        List<SkippedFile> rest = [.. result.SkippedFiles.Where(s => !IsKeptExisting(s))];
        List<SkippedFile> restKept = [.. result.KeptExistingFiles.Where(s => !IsKeptExisting(s))];
        if (anyKept && restKept.Count == 0 && rest.All(s => s.Text?.Code == MessageCode.AllEntriesSkipped))
            rest.Clear();
        return result with { SkippedFiles = rest, KeptExistingFiles = restKept };
    }

    private static bool IsKeptExisting(SkippedFile skipped) => skipped.Text?.Code == MessageCode.FileExistsAtDestination;
}
