using Archiver.Core.Models;

namespace Archiver.App.Core;

/// <summary>
/// T-F342: the method list the "New archive" card shows for 7z. The names are the methods' own,
/// the same in every language.
/// </summary>
public static class SevenZipMethodOption
{
    private static readonly SevenZipMethod[] Methods =
        [SevenZipMethod.Lzma2, SevenZipMethod.Lzma, SevenZipMethod.BZip2, SevenZipMethod.Deflate, SevenZipMethod.Copy];

    /// <summary>The list's rows, in order; LZMA2, the default, first.</summary>
    public static IReadOnlyList<string> Names { get; } = ["LZMA2", "LZMA", "BZip2", "Deflate", "Copy"];

    /// <summary>The method of row <paramref name="index"/>; LZMA2 for a row the list does not have.</summary>
    public static SevenZipMethod FromIndex(int index) =>
        index >= 0 && index < Methods.Length ? Methods[index] : SevenZipMethod.Lzma2;

    /// <summary>The row of <paramref name="method"/>; the first for a method the list does not have.</summary>
    public static int ToIndex(SevenZipMethod method) => Math.Max(Array.IndexOf(Methods, method), 0);

    /// <summary>The row text of <paramref name="method"/>.</summary>
    public static string Name(SevenZipMethod method) => Names[ToIndex(method)];

    /// <summary>
    /// Whether the compression level means anything: not for plain tar (T-F105), nor for a 7z whose
    /// method is Copy.
    /// </summary>
    public static bool HasCompressionLevel(ArchiveContainerFormat format, SevenZipMethod method) =>
        format != ArchiveContainerFormat.Tar && !(format == ArchiveContainerFormat.SevenZip && method == SevenZipMethod.Copy);
}
