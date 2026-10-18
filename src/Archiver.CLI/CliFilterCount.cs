namespace Archiver.CLI;

/// <summary>
/// T-F410: how many entries a name filter let through during one extraction, for 7-Zip's
/// "No files to process". Counted, not inferred from the result: entries that passed and were
/// all skipped at the conflict prompt leave the same empty result as entries that never passed.
/// </summary>
public sealed class CliFilterCount
{
    private int _count;

    public int Count => Volatile.Read(ref _count);

    /// <summary><paramref name="filter"/>, counting every entry it lets through.</summary>
    public Func<string, bool, bool> Counting(Func<string, bool, bool> filter) => (path, isFolder) =>
    {
        bool passes = filter(path, isFolder);
        if (passes)
            Interlocked.Increment(ref _count);
        return passes;
    };
}
