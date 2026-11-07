using System.Text;

namespace Archiver.Core.Services.Sandbox;

/// <summary>How far tar.exe's "-t" names can be trusted to name the entries (T-F441).</summary>
internal enum TarNameTrust
{
    /// <summary>Each listed name is its entry's name.</summary>
    Faithful,

    /// <summary>Nothing shows a name was changed, nor that none was: a repeated name in a format
    /// whose headers Pakko does not read.</summary>
    Suspect,

    /// <summary>A listed name is not its entry's name: tar.exe printed it by best fit, or as '?'.</summary>
    Lossy,
}

/// <summary>A listing's verdict and a name that shows why (null when <see cref="Trust"/> is Faithful).</summary>
internal sealed record TarListingVerdict(TarNameTrust Trust, string? Example)
{
    public static TarListingVerdict Faithful { get; } = new(TarNameTrust.Faithful, null);
}

/// <summary>
/// T-F441/T-F424: the one judge of tar.exe's listed names. tar.exe prints a name in the system
/// ANSI page (T-F465) with best fit ("café" as "cafe" in 1251, "cafā" as "cafa" in 1252, measured
/// on bsdtar 3.8.9 and 3.5.2), and as '?' where no character fits (3.5.2). Such a name still
/// extracts under its true name, but it cannot decide anything: a filter's verdict on it, a
/// selection of it, a duplicate group. Two names that collapse to one line make a selection
/// extract the other file with exit 0. Names decide only after this verdict says they can
/// (docs/DECISIONS.md, "T-F441 and T-F424").
/// </summary>
internal static class TarNames
{
    /// <summary>
    /// T-F418: the entry names of a "-t" listing, each read back from tar.exe's escaping (an escaped
    /// line break stays inside its line, so the lines are split first); null when a line holds an
    /// escape bsdtar never writes. <paramref name="encoding"/>: the one tar.exe's output was decoded
    /// with (<see cref="TarSandboxScope.OutputEncoding"/>). The only reader of a listing's names.
    /// </summary>
    public static string[]? ReadListing(string stdOut, Encoding encoding)
    {
        // T-F419: only the line end is dropped - "a.txt " is an entry of its own, not "a.txt".
        string[] lines = [.. stdOut.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0)];
        string[] names = new string[lines.Length];
        for (int i = 0; i < lines.Length; i++)
        {
            if (TarListingName.Unescape(lines[i], encoding) is not { } name)
                return null;
            names[i] = name;
        }
        return names;
    }

    // T-F465: each listed name (a folder's '/' dropped) and the bytes tar.exe printed for it; null where one
    // of its lines does not decode to it without loss, so that name can never be sent back.
    public static Dictionary<string, byte[]?> ReadListedNameBytes(byte[] listing, System.Text.Encoding encoding)
    {
        var listed = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
        foreach (byte[] line in ListingLines(listing))
        {
            string text = encoding.GetString(line);
            if (TarListingName.Unescape(text, encoding) is not { } name)
                continue; // not a name: the service refuses such a listing before anything is sent
            string key = name.TrimEnd('/');
            byte[]? bytes = IsLossless(line, text, name, encoding) ? encoding.GetBytes(key) : null;
            if (!listed.TryGetValue(key, out byte[]? known))
                listed[key] = bytes;
            else if (known is not null && (bytes is null || !known.AsSpan().SequenceEqual(bytes)))
                listed[key] = null;
        }
        return listed;
    }

    // The listing's non-empty lines, a trailing '\r' dropped.
    private static IEnumerable<byte[]> ListingLines(byte[] listing)
    {
        int start = 0;
        while (start < listing.Length)
        {
            int end = Array.IndexOf(listing, (byte)'\n', start);
            if (end < 0)
                end = listing.Length;
            byte[] line = listing[start..(end > start && listing[end - 1] == (byte)'\r' ? end - 1 : end)];
            start = end + 1;
            if (line.Length > 0)
                yield return line;
        }
    }

    private static bool IsLossless(byte[] line, string text, string name, System.Text.Encoding encoding) =>
        encoding.GetBytes(text).AsSpan().SequenceEqual(line)
        && TarListingName.UnescapeBytes(text, encoding) is { } printed
        && encoding.GetBytes(name).AsSpan().SequenceEqual(printed);

    /// <summary>
    /// The verdict on <paramref name="listed"/> before anything is extracted, for the names that
    /// decide something: <paramref name="deciding"/> (a selection's members) or, when null, every
    /// name (a filter). <paramref name="headerNames"/>: the names a plain or gzip tar's headers
    /// store (<see cref="TarSandboxScope.ReadHeaderNamesAsync"/>), null for any other format.
    /// </summary>
    public static TarListingVerdict Judge(
        IReadOnlyList<string> listed, IReadOnlyList<string>? headerNames, IReadOnlyCollection<string>? deciding = null)
    {
        HashSet<string>? decidingSet = deciding is null ? null : new HashSet<string>(deciding, StringComparer.Ordinal);
        bool Decides(string name) => decidingSet is null || decidingSet.Contains(name);

        // T-F416: a real '?' is no Windows file name either.
        if (listed.Where(Decides).Select(EntryDiskName.StripLeadingDotSlash).FirstOrDefault(name => name.Contains('?')) is { } unshowable)
            return new TarListingVerdict(TarNameTrust.Lossy, unshowable);

        return headerNames is not null ? JudgeByHeaders(listed, headerNames, Decides) : JudgeByRepeats(listed, Decides);
    }

    private static TarListingVerdict JudgeByHeaders(IReadOnlyList<string> listed, IReadOnlyList<string> headerNames, Func<string, bool> decides)
    {
        if (headerNames.Count != listed.Count)
            return new TarListingVerdict(TarNameTrust.Suspect, listed.Where(decides).Select(EntryDiskName.StripLeadingDotSlash).FirstOrDefault());
        for (int i = 0; i < listed.Count; i++)
        {
            // A folder's trailing '/' is the listing's convention, not part of the name.
            if (decides(listed[i]) && !string.Equals(listed[i].TrimEnd('/'), headerNames[i].TrimEnd('/'), StringComparison.Ordinal))
                return new TarListingVerdict(TarNameTrust.Lossy, EntryDiskName.StripLeadingDotSlash(listed[i]));
        }
        return TarListingVerdict.Faithful;
    }

    // Without the headers, two entries listed under one name may be two names tar.exe could not
    // show, or one name stored twice (T-F171): only the files on disk can tell.
    private static TarListingVerdict JudgeByRepeats(IReadOnlyList<string> listed, Func<string, bool> decides)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string name in listed) // NOSONAR: S3267 - the condition adds to the set; a Where would hide that side effect.
        {
            if (!seen.Add(name.TrimEnd('/')) && decides(name))
                return new TarListingVerdict(TarNameTrust.Suspect, EntryDiskName.StripLeadingDotSlash(name));
        }
        return TarListingVerdict.Faithful;
    }

    /// <summary>
    /// The verdict after tar.exe extracted every entry into the quarantine: a file there that no
    /// listed name accounts for was listed under another name. The prediction is the move phase's
    /// own mapping (<see cref="EntryDiskName.AsTarExeWrites"/>, case-insensitive), so a name that
    /// lands differently by design ("a?b" as "a_b", "a.txt." kept) is accounted for.
    /// <paramref name="quarantinedFiles"/>: paths relative to the quarantine's output folder, '/'
    /// between parts. Only valid after a run without a member list: a selection of "cafe.txt"
    /// puts only the real "cafe.txt" on disk, which agrees with any listing.
    /// T-F443: <paramref name="quarantinedFolders"/> the same way, against every listed folder and
    /// every folder a listed name lies in (Pakko pre-creates only those), so a folder listed by best
    /// fit is seen even when no file lies in it.
    /// </summary>
    public static TarListingVerdict JudgeExtracted(
        IReadOnlyList<string> listed, IEnumerable<string> quarantinedFiles, IEnumerable<string> quarantinedFolders)
    {
        var predictedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var predictedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in listed)
        {
            string onDisk = EntryDiskName.AsTarExeWrites(EntryDiskName.StripLeadingDotSlash(name));
            string path = onDisk.TrimEnd('/');
            if (!name.EndsWith('/'))
                predictedFiles.Add(path);
            else if (path.Length > 0)
                predictedFolders.Add(path);
            for (int sep = path.LastIndexOf('/'); sep > 0; sep = path.LastIndexOf('/', sep - 1))
                predictedFolders.Add(path[..sep]);
        }
        string? unaccounted = quarantinedFiles.FirstOrDefault(file => !predictedFiles.Contains(file))
            ?? quarantinedFolders.FirstOrDefault(folder => !predictedFolders.Contains(folder));
        return unaccounted is null ? TarListingVerdict.Faithful : new TarListingVerdict(TarNameTrust.Lossy, unaccounted);
    }

    /// <summary>
    /// Refuses the archive when its names are about to decide something they cannot:
    /// <paramref name="allowSuspect"/> only where a check after a full extraction
    /// (<see cref="JudgeExtracted"/>) still follows.
    /// </summary>
    /// <exception cref="TarArgumentEncodingException">The verdict does not let names decide.</exception>
    public static void EnsureNamesCanDecide(TarListingVerdict verdict, bool allowSuspect = false)
    {
        if (verdict.Trust == TarNameTrust.Faithful || (allowSuspect && verdict.Trust == TarNameTrust.Suspect))
            return;
        throw new TarArgumentEncodingException(verdict.Example ?? string.Empty, (uint)TarOutputEncoding.Current.CodePage);
    }
}
