using System.Runtime.ExceptionServices;
using Archiver.Core.Models;

namespace Archiver.Core.Services;

/// <summary>
/// T-F540: a streamed listing runs its caller's callback inside a reader - in a walk of a ZIP's
/// directory, in the handler of tar.exe's or the archive helper's output lines. What the callback
/// throws is the caller's own (a closed pipe under <c>pakko l | more</c>), never the archive's: it
/// travels through the reader wrapped, past every catch that turns an IO failure into "cannot read
/// the archive", and is thrown again as it was at the public boundary. Cancellation is checked
/// before each entry is passed on and once more at the end.
/// </summary>
internal static class ListingCallback
{
    /// <summary>Runs <paramref name="list"/> with <paramref name="onEntry"/> guarded as above.</summary>
    public static async Task<ArchiveListSummary> RunAsync(
        Action<ArchiveEntryInfo> onEntry, Func<Action<ArchiveEntryInfo>, Task<ArchiveListSummary>> list, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(onEntry);
        ArchiveListSummary summary;
        try
        {
            summary = await list(entry =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                Call(onEntry, entry);
            }).ConfigureAwait(false);
        }
        catch (CallbackFailureException failure)
        {
            failure.Original.Throw();
            throw;
        }
        cancellationToken.ThrowIfCancellationRequested();
        return summary;
    }

    /// <summary>A streamed listing kept whole: the list form of the API.</summary>
    public static async Task<ArchiveListResult> CollectAsync(Func<Action<ArchiveEntryInfo>, Task<ArchiveListSummary>> stream)
    {
        var entries = new List<ArchiveEntryInfo>();
        ArchiveListSummary summary = await stream(entries.Add).ConfigureAwait(false);
        return summary.Success
            ? new ArchiveListResult { Success = true, Entries = entries }
            : CoreMessages.ListFailure(summary);
    }

    private static void Call(Action<ArchiveEntryInfo> onEntry, ArchiveEntryInfo entry)
    {
        try
        {
            onEntry(entry);
        }
        catch (Exception thrown)
        {
            throw new CallbackFailureException(ExceptionDispatchInfo.Capture(thrown));
        }
    }

    // Not an IOException, nor any type a reader catches.
    private sealed class CallbackFailureException(ExceptionDispatchInfo original) : Exception("A listing's callback threw.", original.SourceException) // NOSONAR: S3871 - private on purpose: unwrapped at the boundary, never seen outside this class
    {
        public ExceptionDispatchInfo Original { get; } = original;
    }
}
