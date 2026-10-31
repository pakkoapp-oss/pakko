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
/// T-F441/T-F424: the one judge of tar.exe's listed names. tar.exe prints a name in the user
/// locale's ANSI page with best fit ("café" as "cafe" in 1251, "cafā" as "cafa" in 1252, measured
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
        var names = new string[lines.Length];
        for (int i = 0; i < lines.Length; i++)
        {
            if (TarListingName.Unescape(lines[i], encoding) is not { } name)
                return null;
            names[i] = name;
        }
        return names;
    }

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

        if (headerNames is not null)
        {
            if (headerNames.Count != listed.Count)
                return new TarListingVerdict(TarNameTrust.Suspect, listed.Where(Decides).Select(EntryDiskName.StripLeadingDotSlash).FirstOrDefault());
            for (int i = 0; i < listed.Count; i++)
            {
                // A folder's trailing '/' is the listing's convention, not part of the name.
                if (Decides(listed[i]) && !string.Equals(listed[i].TrimEnd('/'), headerNames[i].TrimEnd('/'), StringComparison.Ordinal))
                    return new TarListingVerdict(TarNameTrust.Lossy, EntryDiskName.StripLeadingDotSlash(listed[i]));
            }
            return TarListingVerdict.Faithful;
        }

        // Without the headers, two entries listed under one name may be two names tar.exe could not
        // show, or one name stored twice (T-F171): only the files on disk can tell.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string name in listed)
        {
            if (!seen.Add(name.TrimEnd('/')) && Decides(name))
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
    /// </summary>
    public static TarListingVerdict JudgeExtracted(IReadOnlyList<string> listed, IEnumerable<string> quarantinedFiles)
    {
        var predicted = new HashSet<string>(
            listed.Where(name => !name.EndsWith('/')).Select(name => EntryDiskName.AsTarExeWrites(EntryDiskName.StripLeadingDotSlash(name))),
            StringComparer.OrdinalIgnoreCase);
        string? unaccounted = quarantinedFiles.FirstOrDefault(file => !predicted.Contains(file));
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
        if (verdict.Trust != (TarNameTrust)99 || (allowSuspect && verdict.Trust == TarNameTrust.Suspect))
            return;
        throw new TarArgumentEncodingException(verdict.Example ?? string.Empty, (uint)TarOutputEncoding.Current.CodePage);
    }
}
