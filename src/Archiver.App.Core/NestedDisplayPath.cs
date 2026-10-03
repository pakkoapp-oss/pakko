namespace Archiver.App.Core;

/// <summary>
/// T-F220 item 5: a nested archive is browsed from a temp copy (<see cref="NestedArchiveCache"/>);
/// text shown to the user names the chain the user drilled through instead of that copy's path.
/// </summary>
public static class NestedDisplayPath
{
    /// <summary>"outer.zip > docs > l4.zip" from the breadcrumb's segments.</summary>
    public static string Chain(IEnumerable<string> segments) => string.Join(" > ", segments);

    /// <summary><paramref name="text"/> with <paramref name="tempArchivePath"/> replaced by
    /// <paramref name="chain"/>; unchanged when nothing is nested (null).</summary>
    public static string Map(string text, string? tempArchivePath, string chain) =>
        string.IsNullOrEmpty(tempArchivePath) ? text : text.Replace(tempArchivePath, chain, StringComparison.OrdinalIgnoreCase);
}
