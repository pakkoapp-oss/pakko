using Archiver.Core.Models;

namespace Archiver.Core.Services;

// T-F158: shared with ZipArchiveService.ArchiveSingleArchiveModeAsync/ResolveSeparateArchivePlansAsync
// and TarSandboxedService's own archive-creation conflict handling — replaces three hand-duplicated
// copies of the same "does this destination archive path already exist / collide with another
// source in this same batch" decision, the archive-creation-side analogue of T-F157's
// ExtractionDestinationPlanner.
internal enum DestinationConflictOutcome { Proceed, ProceedAfterDeletingExisting, Skip }

internal static class DestinationConflictResolver
{
    // Pure aside from awaiting conflictResolver (which may prompt the user — a genuine query, not
    // an action taken on its own) and calling renameCandidate (the caller's own GetUniqueFilePath,
    // unchanged either engine's side). Deliberately does no File.Exists/File.Delete itself —
    // callers already know how to compute onDiskConflict/sameRunConflict in their own context, and
    // act on ProceedAfterDeletingExisting themselves. Keeping I/O out of this function is what
    // makes it unit-testable the way ExtractionDestinationPlanner.Resolve is (T-F157) — Tar's
    // original ResolveDestinationConflictAsync had File.Delete inside it and was never directly
    // unit-tested for exactly that reason.
    public static async Task<(DestinationConflictOutcome Outcome, string ResolvedDestPath)> ResolveAsync(
        string destPath, bool onDiskConflict, bool sameRunConflict,
        ConflictResolver conflictResolver, Func<string, string> renameCandidate)
    {
        if (!onDiskConflict && !sameRunConflict)
            return (DestinationConflictOutcome.Proceed, destPath);

        return await conflictResolver.ResolveAsync(destPath).ConfigureAwait(false) switch
        {
            ConflictBehavior.Skip => (DestinationConflictOutcome.Skip, destPath),
            // A same-run collision (sameRunConflict) is renamed rather than deleted even under
            // Overwrite — the on-disk file (if any) may not even exist yet (another in-flight
            // worker owns creating it), so there is nothing safe to delete; matches
            // ZipArchiveService.ResolveSeparateArchivePlansAsync's pre-T-F158 behavior exactly.
            ConflictBehavior.Overwrite => onDiskConflict && !sameRunConflict
                ? (DestinationConflictOutcome.ProceedAfterDeletingExisting, destPath)
                : (DestinationConflictOutcome.Proceed, renameCandidate(destPath)),
            _ => (DestinationConflictOutcome.Proceed, renameCandidate(destPath)), // Rename, or any
                // future ConflictBehavior value — matches ArchiveNaming.GetExtension's existing
                // discard convention; T-F157 confirmed a discard-less enum switch doesn't get real
                // compiler exhaustiveness here, so not chasing that again.
        };
    }
}
