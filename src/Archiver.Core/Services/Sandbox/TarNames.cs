using System.Text;
using Archiver.Core.Text;

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
    // of its lines does not decode to it without loss, or two lines print it in different bytes, so that
    // name can never be sent back. T-F467: the printed bytes need not be the page's own form of the
    // name - tar.exe prints a raw 932 name in the form the archive stored (NEC's 0x87 0x90, measured).
    // Each line's printed bytes also count under the text they read as in the page, which tar.exe
    // matches by: "" plus a raw 0xBE is U+222A, as NEC's 0x87 0x9C is, though the text unescape reads
    // it as two other characters (fresh review). A line whose printed bytes are no text there makes no
    // name sendable: what tar.exe would match it with is unknown.
    public static Dictionary<string, byte[]?> ReadListedNameBytes(byte[] listing, System.Text.Encoding encoding)
    {
        var listed = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
        bool unknownText = false;
        foreach (byte[] line in ListingLines(listing))
        {
            string text = encoding.GetString(line);
            if (TarListingName.Unescape(text, encoding) is not { } name)
                continue; // not a name: the service refuses such a listing before anything is sent
            string key = name.TrimEnd('/');
            byte[]? bytes = PrintedBytes(line, text, name, encoding) is { } printed ? printed[..^(name.Length - key.Length)] : null;
            Add(listed, key, bytes);
            if (UnescapePrinted(line, encoding) is not { } raw
                || TarCommandLineEncoding.Decode(raw, (uint)encoding.CodePage) is not { } rawText)
            {
                unknownText = true;
                continue;
            }
            string rawKey = rawText.TrimEnd('/');
            if (!string.Equals(rawKey, key, StringComparison.Ordinal))
                Add(listed, rawKey, raw[..^(rawText.Length - rawKey.Length)]);
        }
        if (unknownText)
        {
            foreach (string key in listed.Keys.ToList())
                listed[key] = null;
        }
        return listed;
    }

    private static void Add(Dictionary<string, byte[]?> listed, string key, byte[]? bytes)
    {
        if (!listed.TryGetValue(key, out byte[]? known))
            listed[key] = bytes;
        else if (known is not null && (bytes is null || !known.AsSpan().SequenceEqual(bytes)))
            listed[key] = null;
    }

    /// <summary>
    /// T-F467: the listed entries by the text their printed bytes read as in tar.exe's page
    /// (<paramref name="commandLinePage"/>, the system's own table); null where two entries print that
    /// text in different bytes. Bytes that are no text in that page are left out: they read as no name
    /// Pakko sends.
    /// </summary>
    public static Dictionary<string, byte[]?> IndexByText(Dictionary<string, byte[]?> listed, uint commandLinePage)
    {
        var byText = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
        foreach (byte[]? printed in listed.Values)
        {
            if (printed is null || TarCommandLineEncoding.Decode(printed, commandLinePage) is not { } text)
                continue;
            if (!byText.TryGetValue(text, out byte[]? known))
                byText[text] = printed;
            else if (known is not null && !known.AsSpan().SequenceEqual(printed))
                byText[text] = null;
        }
        return byText;
    }

    /// <summary>
    /// T-F465/T-F467: whether <paramref name="sent"/>, the bytes Pakko would send to tar.exe for the
    /// listed name <paramref name="key"/>, takes exactly the entry tar.exe printed under it. tar.exe
    /// matches a "-T"/"-X"/"-q" name as text in its own page (measured on 932: the canonical 0x81 0xE0
    /// took the entry printed as NEC's 0x87 0x90), so both must read as the same text in
    /// <paramref name="commandLinePage"/> (the system's own table), and that text must be printed in no other bytes
    /// (<paramref name="byText"/>, from <see cref="IndexByText"/>). A listing read in another page fails
    /// here (T-F465's swap): its printed bytes are another text in tar.exe's page.
    /// </summary>
    public static bool ReachesTheListedEntry(
        string key, Dictionary<string, byte[]?> listed, Dictionary<string, byte[]?> byText, uint commandLinePage, byte[] sent) =>
        listed.TryGetValue(key, out byte[]? printed) && printed is not null
        && TarCommandLineEncoding.Decode(printed, commandLinePage) is { } text
        && TarCommandLineEncoding.Decode(sent, commandLinePage) == text
        && byText.TryGetValue(text, out byte[]? only) && only is not null && only.AsSpan().SequenceEqual(printed);

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

    // The bytes tar.exe printed for name, or null when the line or those bytes do not decode to it
    // without loss. A folder's trailing '/' is one byte in every page, so the caller trims it by count.
    private static byte[]? PrintedBytes(byte[] line, string text, string name, System.Text.Encoding encoding) =>
        TarCommandLineEncoding.Decode(line, (uint)encoding.CodePage) == text
        && UnescapePrinted(line, encoding) is { } printed
        && TarCommandLineEncoding.Decode(printed, (uint)encoding.CodePage) == name
            ? printed
            : null;

    // The line with tar.exe's escapes resolved and every other byte kept as printed: re-encoding the
    // text would turn a raw NEC-selected 0xED 0x40 into the page's 0xFA 0x5C, and two forms of one
    // name would look alike. A '' that is a double-byte character's second byte is not an escape.
    private static byte[]? UnescapePrinted(byte[] line, System.Text.Encoding encoding)
    {
        var bytes = new List<byte>(line.Length);
        uint codePage = (uint)encoding.CodePage;
        int i = 0;
        while (i < line.Length)
        {
            if (line[i] != (byte)'\\')
            {
                int width = TarCommandLineEncoding.IsLeadByte(codePage, line[i]) && i + 1 < line.Length ? 2 : 1;
                bytes.AddRange(line.AsSpan(i, width));
                i += width;
                continue;
            }
            int length = i + 3 < line.Length && IsOctalDigit(line[i + 1]) && IsOctalDigit(line[i + 2]) && IsOctalDigit(line[i + 3]) ? 4 : 2;
            if (i + length > line.Length
                || TarListingName.UnescapeBytes(System.Text.Encoding.ASCII.GetString(line, i, length), encoding) is not { Length: 1 } escaped)
                return null;
            bytes.Add(escaped[0]);
            i += length;
        }
        return [.. bytes];
    }

    private static bool IsOctalDigit(byte value) => value is >= (byte)'0' and <= (byte)'7';

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
        // T-F466: libarchive composes a name it converts from UTF-8 or UTF-16 to NFC (archive_string.c,
        // SCONV_NORMALIZATION_C) and lists a legacy page's name as stored, so a listed name agrees
        // with its header as stored or in NFC (measured on 3.8.9 and 3.5.2). Two different stored
        // names listed alike are taken together by "-T" (measured): such a name cannot decide.
        // Normalize cannot throw: header names come from valid UTF-8 or a code page's decoder,
        // neither of which yields a lone surrogate.
        var storedFor = new Dictionary<string, string>(StringComparer.Ordinal);
        var ambiguous = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < listed.Count; i++)
        {
            // A folder's trailing '/' is the listing's convention, not part of the name.
            string name = listed[i].TrimEnd('/');
            string stored = headerNames[i].TrimEnd('/');
            bool agrees = string.Equals(name, stored, StringComparison.Ordinal)
                || string.Equals(name, stored.Normalize(NormalizationForm.FormC), StringComparison.Ordinal);
            if (decides(listed[i]) && !agrees)
                return new TarListingVerdict(TarNameTrust.Lossy, EntryDiskName.StripLeadingDotSlash(listed[i]));
            if (!storedFor.TryAdd(name, stored) && !string.Equals(storedFor[name], stored, StringComparison.Ordinal))
                ambiguous.Add(name);
        }
        if (listed.FirstOrDefault(entry => decides(entry) && ambiguous.Contains(entry.TrimEnd('/'))) is { } shared)
            return new TarListingVerdict(TarNameTrust.Lossy, EntryDiskName.StripLeadingDotSlash(shared));
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
        throw new TarArgumentEncodingException(verdict.Example ?? string.Empty, (uint)TextBoundaries.TarExeOutput(MachinePages.Current).CodePage);
    }
}
