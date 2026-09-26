using System.Collections.ObjectModel;
using System.IO.Compression;
using Archiver.Core.Services.Zip.Decryption;

namespace Archiver.Core.Services.Zip;

/// <summary>
/// T-F234: one ZIP entry with the name Pakko uses for it everywhere (list, extract, test, scan) —
/// decoded by 7-Zip's rule, never <see cref="ZipArchiveEntry.FullName"/>, which .NET decodes as
/// UTF-8 for every entry without the UTF-8 flag.
/// </summary>
/// <param name="Entry">The .NET entry, for its data, sizes and CRC-32.</param>
/// <param name="FullName">The decoded name, '/'-separated.</param>
/// <param name="CollidesAfterDecoding">True when an earlier entry has different raw name bytes but
/// the same decoded name — writing both would silently lose one.</param>
internal sealed record NamedZipEntry(ZipArchiveEntry Entry, string FullName, bool CollidesAfterDecoding);

/// <summary>
/// T-F234: opens a ZIP for reading and pairs every <see cref="ZipArchiveEntry"/> with its decoded
/// name. Both lists come from the same central directory in the same order; a count mismatch means
/// the two readers disagree about the archive, which is reported as a corrupted archive rather than
/// falling back to .NET's UTF-8 names (that fallback is the bug this type exists to fix).
/// </summary>
internal sealed class ZipArchiveReader : IDisposable
{
    public ZipArchive Archive { get; }
    public IReadOnlyList<NamedZipEntry> Entries { get; }

    private ZipArchiveReader(ZipArchive archive, IReadOnlyList<NamedZipEntry> entries)
    {
        Archive = archive;
        Entries = entries;
    }

    public static ZipArchiveReader Open(string path, ZipNameCodePages codePages)
    {
        var archive = ZipFile.OpenRead(path);
        try
        {
            List<(byte[] RawName, string Name)> names;
            using (var raw = File.OpenRead(path))
                names = RawZipEntryLocator.ReadEntryNames(raw, codePages);

            if (names.Count != archive.Entries.Count)
                throw new InvalidDataException("ZIP central directory entry count mismatch while reading entry names.");

            return new ZipArchiveReader(archive, Pair(archive.Entries, names));
        }
        catch
        {
            archive.Dispose();
            throw;
        }
    }

    private static List<NamedZipEntry> Pair(
        ReadOnlyCollection<ZipArchiveEntry> entries, List<(byte[] RawName, string Name)> names)
    {
        var firstRawByName = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var result = new List<NamedZipEntry>(entries.Count);
        for (int i = 0; i < entries.Count; i++)
        {
            var (rawName, name) = names[i];
            bool collides = false;
            if (!name.EndsWith('/'))
            {
                if (firstRawByName.TryGetValue(name, out byte[]? firstRaw))
                    collides = !firstRaw.AsSpan().SequenceEqual(rawName);
                else
                    firstRawByName[name] = rawName;
            }
            result.Add(new NamedZipEntry(entries[i], name, collides));
        }
        return result;
    }

    public void Dispose() => Archive.Dispose();
}
