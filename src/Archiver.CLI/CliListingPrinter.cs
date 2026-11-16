using Archiver.Core.Models;

namespace Archiver.CLI;

/// <summary>
/// T-F540: one archive's 'l' rows printed as the listing reads them, never held. The header (or
/// -slt's archive block) goes out before the first row, or at the end of a listing that succeeded
/// with no row: a listing that fails before its first row prints nothing, as before. One that fails
/// after some rows leaves them printed - the caller then reports the error, as 7-Zip does.
/// </summary>
internal static class CliListingPrinter
{
    /// <param name="list">The streamed listing, given the callback that prints a row.</param>
    /// <param name="header">The lines before the first row; null for none (-ba).</param>
    /// <param name="includes">-i/-x: whether an entry is a row at all.</param>
    /// <param name="rowLines">An entry's lines: one row, or its -slt block.</param>
    /// <returns>How the listing ended, and the rows printed.</returns>
    public static async Task<(ArchiveListSummary Summary, long Printed)> PrintAsync(
        Func<Action<ArchiveEntryInfo>, Task<ArchiveListSummary>> list,
        IReadOnlyList<string>? header,
        Func<ArchiveEntryInfo, bool> includes,
        Func<ArchiveEntryInfo, IReadOnlyList<string>> rowLines,
        TextWriter output)
    {
        bool headerDue = header is not null;
        long printed = 0;
        void Header()
        {
            if (!headerDue)
                return;
            headerDue = false;
            foreach (string line in header!)
                output.WriteLine(line);
        }

        ArchiveListSummary summary = await list(entry =>
        {
            if (!includes(entry))
                return;
            Header();
            foreach (string line in rowLines(entry))
                output.WriteLine(line);
            printed++;
        }).ConfigureAwait(false);
        if (summary.Success)
            Header();
        return (summary, printed);
    }
}
