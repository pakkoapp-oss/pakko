namespace Archiver.Core.Services.Sandbox;

/// <summary>
/// T-F541: what tar.exe's pre-scan read of an archive, held once: every entry's name as "-t" gives it
/// (a folder's '/' kept, T-F455) and, by the same index, its declared size (-1 for a folder). Every
/// later step refers to these by index or enumerates them; none keeps a list or a dictionary of its own.
/// </summary>
internal sealed class TarCatalog(long totalDeclaredSize, string[] names, long[] sizes)
{
    /// <summary>The sum of every file entry's declared size (T-F94).</summary>
    public long TotalDeclaredSize { get; } = totalDeclaredSize;

    public string[] Names { get; } = names;

    public int Count => Names.Length;

    public bool IsFile(int index) => sizes[index] >= 0;

    public long SizeOf(int index) => sizes[index];

    /// <summary>The file entries in archive order, repeats included (T-F171).</summary>
    public IEnumerable<(string Name, long Size)> Files()
    {
        for (int i = 0; i < Names.Length; i++)
        {
            if (sizes[i] >= 0)
                yield return (Names[i], sizes[i]);
        }
    }
}
