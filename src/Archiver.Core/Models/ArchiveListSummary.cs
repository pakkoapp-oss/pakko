using Archiver.Core.Services;

namespace Archiver.Core.Models;

/// <summary>
/// T-F540: how a streamed listing ended. The entries themselves went to the caller's callback as
/// they were read, in archive order, and were not kept. When <see cref="Success"/> is false the
/// caller discards every entry it was given: they may be part of the archive, or rows a check at
/// the end refused.
/// </summary>
public sealed record ArchiveListSummary
{
    public bool Success { get; init; }

    /// <summary>The entries passed to the callback.</summary>
    public long Count { get; init; }

    public string? ErrorMessage { get; init; }

    /// <summary>The error as a code a frontend renders in the user's language (T-F209).</summary>
    public CoreText? ErrorText { get; init; }

    /// <summary>Passes a listing made whole on to <paramref name="onEntry"/>, in order: an engine that lists one entry, or a test's fake.</summary>
    public static ArchiveListSummary Replay(ArchiveListResult listed, Action<ArchiveEntryInfo> onEntry)
    {
        ArgumentNullException.ThrowIfNull(listed);
        ArgumentNullException.ThrowIfNull(onEntry);
        if (!listed.Success)
            return CoreMessages.Summary(listed);
        foreach (ArchiveEntryInfo entry in listed.Entries)
            onEntry(entry);
        return new ArchiveListSummary { Success = true, Count = listed.Entries.Count };
    }
}
