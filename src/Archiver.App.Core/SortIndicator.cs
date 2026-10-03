namespace Archiver.App.Core;

/// <summary>
/// T-F220 item 4: the arrow a column header shows once the list is sorted by it — Segoe MDL2
/// ChevronUp (ascending) or ChevronDown (descending); nothing on the other columns, and nothing
/// before the first click (the list keeps the order items were added in until then).
/// </summary>
public static class SortIndicator
{
    /// <summary>Segoe MDL2 Assets ChevronUp (U+E70E).</summary>
    public static readonly string Ascending = ((char)0xE70E).ToString();

    /// <summary>Segoe MDL2 Assets ChevronDown (U+E70D).</summary>
    public static readonly string Descending = ((char)0xE70D).ToString();

    /// <summary>The glyph for <paramref name="column"/>; empty unless the list is sorted by it.</summary>
    public static string Glyph(string column, string? sortedColumn, bool ascending) =>
        column == sortedColumn ? (ascending ? Ascending : Descending) : string.Empty;
}
