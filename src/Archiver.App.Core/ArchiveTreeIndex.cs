using System.Runtime.InteropServices;
using Archiver.Core.Models;

namespace Archiver.App.Core;

/// <summary>
/// Builds an <see cref="ArchiveTree"/> from a listing, once per archive open. Lives in
/// Archiver.App.Core (not Archiver.Core) because Archiver.Core has zero WinUI/UI-model
/// references — a folder hierarchy is an App-layer concern.
/// </summary>
public static class ArchiveTreeIndex
{
    /// <summary>Builds the folder tree of a listing kept whole.</summary>
    public static ArchiveTree Build(IReadOnlyList<ArchiveEntryInfo> flatEntries)
    {
        var builder = new Builder();
        foreach (ArchiveEntryInfo entry in flatEntries)
            builder.Add(entry);
        return builder.Build();
    }

    /// <summary>
    /// T-F535: takes a streamed listing's entries as they come - each one's path (the same string,
    /// not a copy) and the fields a row shows, never the entry itself - and the encryption summary
    /// with them. Not thread-safe: one listing adds, then <see cref="Build"/> once.
    /// </summary>
    public sealed class Builder
    {
        private readonly List<ArchiveTree.Row> _rows = [];
        private readonly EncryptionSummary.Tally _encryption = new();

        /// <summary>Takes one entry of the listing, in archive order.</summary>
        public void Add(ArchiveEntryInfo entry)
        {
            ArgumentNullException.ThrowIfNull(entry);
            _rows.Add(new ArchiveTree.Row(entry));
            _encryption.Add(entry);
        }

        /// <summary>The listing's encryption, as <see cref="EncryptionSummary.Of"/> gives it.</summary>
        public EncryptionSummary Encryption => _encryption.Summary();

        /// <summary>Sorts the index: N log N, off the UI thread for a large archive.</summary>
        public ArchiveTree Build() => new([.. _rows]);
    }
}

/// <summary>
/// An archive's folder tree. Many ZIPs have no explicit directory entries — folders are implied
/// by '/' in file paths — while tar-family listings carry them; both shapes give the same tree,
/// and the first entry at a path wins. T-F535: no node per folder - the entries sorted by path
/// (ordinal, then archive order) put a folder's whole subtree in one range, found by binary
/// search; a folder's row list is built on first request, so only visited folders pay for rows.
/// </summary>
public sealed class ArchiveTree
{
    private readonly Row[] _rows;
    private readonly int[] _sorted;
    private readonly Dictionary<string, IReadOnlyList<ArchiveEntryViewModel>?> _opened = new(StringComparer.Ordinal);

    // One entry: what a row shows, its optional fields as values beside one byte of which are
    // present (40 bytes, not 72 with a Nullable each). Its key is Path with a leading '/' left out
    // (the root's own).
    [StructLayout(LayoutKind.Auto)]
    internal readonly struct Row
    {
        private const byte HasCompressedSize = 1, HasCrc32 = 2, HasModified = 4, HasEncryption = 8, TimeOfDay = 16, Folder = 32;
        private readonly long _compressedSize;
        private readonly long _modifiedTicks;
        private readonly uint _crc32;
        private readonly EntryEncryption _encryption;
        private readonly DateTimeKind _modifiedKind;
        private readonly byte _flags;

        public Row(ArchiveEntryInfo entry)
        {
            Path = entry.Path;
            Size = entry.Size;
            _compressedSize = entry.CompressedSize ?? 0;
            _crc32 = entry.Crc32 ?? 0;
            _modifiedTicks = entry.Modified?.Ticks ?? 0;
            _modifiedKind = entry.Modified?.Kind ?? DateTimeKind.Unspecified;
            _encryption = entry.Encryption ?? EntryEncryption.None;
            _flags = (byte)((entry.CompressedSize is null ? 0 : HasCompressedSize) | (entry.Crc32 is null ? 0 : HasCrc32)
                | (entry.Modified is null ? 0 : HasModified) | (entry.Encryption is null ? 0 : HasEncryption)
                | (entry.ModifiedHasTime ? TimeOfDay : 0) | (entry.IsDirectory ? Folder : 0));
        }

        public string Path { get; }
        public long Size { get; }
        public long? CompressedSize => Has(HasCompressedSize) ? _compressedSize : null;
        public uint? Crc32 => Has(HasCrc32) ? _crc32 : null;
        public DateTime? Modified => Has(HasModified) ? new DateTime(_modifiedTicks, _modifiedKind) : null;
        public EntryEncryption? Encryption => Has(HasEncryption) ? _encryption : null;
        public bool ModifiedHasTime => Has(TimeOfDay);
        public bool IsDirectory => Has(Folder);
        public ReadOnlySpan<char> Key => Path.AsSpan(FirstSegmentStart(Path));

        private bool Has(byte flag) => (_flags & flag) != 0;
    }

    internal ArchiveTree(Row[] rows)
    {
        _rows = rows;
        _sorted = [.. Enumerable.Range(0, rows.Length)];
        // Stable by construction: equal keys keep archive order, so the first of them is the first entry.
        Array.Sort(_sorted, (a, b) =>
        {
            int byKey = _rows[a].Key.SequenceCompareTo(_rows[b].Key);
            return byKey != 0 ? byKey : a.CompareTo(b);
        });
    }

    /// <summary>
    /// The rows of <paramref name="folderPath"/> ('/'-separated, empty for the root), folders first,
    /// then files, both ordinal — File Explorer's order. False for a folder with no children.
    /// </summary>
    public bool TryGetChildren(string folderPath, out IReadOnlyList<ArchiveEntryViewModel> children)
    {
        if (!_opened.TryGetValue(folderPath, out IReadOnlyList<ArchiveEntryViewModel>? rows))
            _opened[folderPath] = rows = BuildRows(folderPath);
        children = rows ?? [];
        return rows is not null;
    }

    // A leading '/' belongs to the root — its empty first segment would otherwise be a folder
    // whose path, "", is the root's own.
    private static int FirstSegmentStart(string path) => path.Length > 1 && path[0] == '/' ? 1 : 0;

    // The folder's children: each distinct next segment of the keys below it once. A segment's own
    // entries (key P+s) and its subtree (keys P+s+"/...") are two ranges, not one: ' ' and other
    // characters below '/' sort between them ("a/b", "a/b c", "a/b/x").
    private List<ArchiveEntryViewModel>? BuildRows(string folderPath)
    {
        string prefix = folderPath.Length == 0 ? string.Empty : folderPath[FirstSegmentStart(folderPath)..] + "/";
        (int low, int high) = PrefixRange(prefix);
        if (low == high)
            return null;
        var rows = new List<ArchiveEntryViewModel>();
        int at = low;
        while (at < high)
        {
            ReadOnlySpan<char> rest = _rows[_sorted[at]].Key[prefix.Length..];
            int slash = rest.IndexOf('/');
            string segment = (slash < 0 ? rest : rest[..slash]).ToString();
            string own = prefix + segment;
            (int ownLow, int ownHigh) = ExactRange(own);
            (int belowLow, int belowHigh) = PrefixRange(own + "/");
            if (slash >= 0 && ownLow < ownHigh)
            {
                // Its own entry came first in the sorted order and made the row already.
                at = belowHigh;
                continue;
            }
            int firstOwn = ownLow < ownHigh ? _sorted[ownLow] : int.MaxValue;
            int firstBelow = FirstInArchiveOrder(belowLow, belowHigh);
            rows.Add(firstOwn < firstBelow
                ? ToRow(_rows[firstOwn], segment)
                : ImpliedFolder(_rows[firstBelow].Path, FirstSegmentStart(_rows[firstBelow].Path) + own.Length, segment));
            at = slash < 0 ? ownHigh : belowHigh;
        }
        rows.Sort((a, b) =>
        {
            if (a.IsFolder != b.IsFolder)
                return a.IsFolder ? -1 : 1;
            return string.CompareOrdinal(a.Name, b.Name);
        });
        return rows;
    }

    private int FirstInArchiveOrder(int low, int high)
    {
        int first = int.MaxValue;
        for (int i = low; i < high; i++)
            first = Math.Min(first, _sorted[i]);
        return first;
    }

    // The sorted positions whose key starts with prefix: one range, since they sort together.
    private (int Low, int High) PrefixRange(string prefix)
    {
        int low = LowerBound(prefix);
        return (low, SearchBoundary(low, _sorted.Length, prefix));
    }

    // The first position in [from, to) whose key no longer starts with prefix; to when all do.
    private int SearchBoundary(int from, int to, string prefix)
    {
        while (from < to)
        {
            int middle = from + ((to - from) / 2);
            if (_rows[_sorted[middle]].Key.StartsWith(prefix, StringComparison.Ordinal))
                from = middle + 1;
            else
                to = middle;
        }
        return from;
    }

    private (int Low, int High) ExactRange(string key)
    {
        int low = LowerBound(key);
        int high = low;
        while (high < _sorted.Length && _rows[_sorted[high]].Key.SequenceEqual(key))
            high++;
        return (low, high);
    }

    // The first sorted position whose key is not below key.
    private int LowerBound(string key)
    {
        int low = 0;
        int high = _sorted.Length;
        while (low < high)
        {
            int middle = low + ((high - low) / 2);
            if (_rows[_sorted[middle]].Key.SequenceCompareTo(key) < 0)
                low = middle + 1;
            else
                high = middle;
        }
        return low;
    }

    private static ArchiveEntryViewModel ToRow(Row entry, string name) => new()
    {
        FullPath = entry.Path,
        Name = name,
        IsFolder = entry.IsDirectory,
        Size = entry.Size,
        CompressedSize = entry.CompressedSize,
        Crc32 = entry.Crc32,
        Modified = entry.Modified,
        ModifiedHasTime = entry.ModifiedHasTime,
        Encryption = entry.Encryption,
    };

    // A folder no entry names: its path is the first entry below it, cut where the folder ends.
    private static ArchiveEntryViewModel ImpliedFolder(string firstPathBelow, int end, string name) =>
        new() { FullPath = firstPathBelow[..end], Name = name, IsFolder = true };
}
