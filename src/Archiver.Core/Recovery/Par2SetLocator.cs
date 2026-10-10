using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace Archiver.Core.Recovery;

/// <summary>A set chosen for a file; <see cref="NameMatches"/> false means it matched by content
/// alone (the archive or the set was renamed), which a frontend reports as a warning.</summary>
internal sealed record Par2Match(Par2Set Set, bool NameMatches);

/// <summary>
/// Which PAR2 files belong together and which file they protect (T-F275). A set is named by its
/// base: <c>&lt;base&gt;.par2</c> plus <c>&lt;base&gt;.*.par2</c> in the same folder, the base being
/// a PAR2 file's name without <c>.par2</c> and a <c>.volN+M</c> before it (par2cmdline's
/// <c>LoadPacketsFromOtherFiles</c>). The protected file always comes from the path the user gave
/// or this rule, never from the name inside the set.
/// </summary>
internal static partial class Par2SetLocator
{
    private const string Extension = ".par2";

    /// <summary>The base of a PAR2 file name, or null when the name does not end in .par2.</summary>
    internal static string? BaseName(string par2FileName)
    {
        if (!par2FileName.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) || par2FileName.Length == Extension.Length)
            return null;
        string withoutExtension = par2FileName[..^Extension.Length];
        return VolumeSuffix().Replace(withoutExtension, string.Empty, 1);
    }

    /// <summary><c>&lt;base&gt;.par2</c> and <c>&lt;base&gt;.*.par2</c> files in <paramref name="folder"/>.</summary>
    internal static IReadOnlyList<string> SetFiles(string folder, string baseName)
    {
        if (!Directory.Exists(folder))
            return [];
        var files = new List<string>();
        foreach (string path in Directory.EnumerateFiles(folder))
        {
            string name = Path.GetFileName(path);
            if (name.Length >= baseName.Length + Extension.Length
                && name.StartsWith(baseName, StringComparison.OrdinalIgnoreCase)
                && name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)
                && name[baseName.Length] == '.')
                files.Add(path);
        }
        files.Sort(StringComparer.OrdinalIgnoreCase);
        return files;
    }

    /// <summary>The set files of a PAR2 file the user opened.</summary>
    internal static IReadOnlyList<string> SetFilesForPar2(string par2Path)
    {
        string? baseName = BaseName(Path.GetFileName(par2Path));
        return baseName is null ? [] : SetFiles(Path.GetDirectoryName(Path.GetFullPath(par2Path))!, baseName);
    }

    /// <summary>The set files next to a file: base <c>X.ext</c> first, then <c>X</c> (QuickPar and
    /// MultiPar name a set without the last extension).</summary>
    internal static IReadOnlyList<string> SetFilesForTarget(string targetPath)
    {
        string full = Path.GetFullPath(targetPath);
        string folder = Path.GetDirectoryName(full)!;
        string name = Path.GetFileName(full);
        IReadOnlyList<string> files = SetFiles(folder, name);
        if (files.Count > 0)
            return files;
        string shortName = Path.GetFileNameWithoutExtension(name);
        return shortName.Length > 0 && shortName != name ? SetFiles(folder, shortName) : [];
    }

    /// <summary>The files a set opened through <paramref name="par2Path"/> may protect: the base
    /// itself, whether it exists or not, then any other <c>&lt;base&gt;.*</c> file that is not a
    /// PAR2 file.</summary>
    internal static IReadOnlyList<string> TargetCandidates(string par2Path)
    {
        string full = Path.GetFullPath(par2Path);
        string? baseName = BaseName(Path.GetFileName(full));
        if (baseName is null)
            return [];
        string folder = Path.GetDirectoryName(full)!;
        var candidates = new List<string> { Path.Combine(folder, baseName) };
        foreach (string path in Directory.EnumerateFiles(folder))
        {
            string name = Path.GetFileName(path);
            if (name.Length > baseName.Length + 1
                && name.StartsWith(baseName, StringComparison.OrdinalIgnoreCase)
                && name[baseName.Length] == '.'
                && !name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
                candidates.Add(path);
        }
        candidates.Sort(1, candidates.Count - 1, StringComparer.OrdinalIgnoreCase);
        return candidates;
    }

    /// <summary>
    /// The set that protects <paramref name="targetPath"/>: its name matches, or its length and
    /// first-16-KiB MD5 do. A set matching both wins over one matching by content, which wins over
    /// one matching by name; then the one with more recovery blocks. Null when none matches.
    /// </summary>
    internal static Par2Match? Select(IReadOnlyList<Par2Set> sets, string targetPath)
    {
        string name = Path.GetFileName(targetPath);
        (long Length, UInt128 Md5First16k)? content = ReadContentKey(targetPath);
        Par2Match? best = null;
        int bestScore = 0;
        foreach (Par2Set set in sets)
        {
            bool nameMatches = string.Equals(Encoding.UTF8.GetString(set.Name), name, StringComparison.OrdinalIgnoreCase);
            bool contentMatches = content is { } key && key.Length == set.FileLength && key.Md5First16k == set.Md5First16k;
            int score = (contentMatches ? 2 : 0) + (nameMatches ? 1 : 0);
            if (score == 0)
                continue;
            if (score > bestScore || (score == bestScore && set.RecoveryBlocks.Count > best!.Set.RecoveryBlocks.Count))
            {
                best = new Par2Match(set, nameMatches);
                bestScore = score;
            }
        }
        return best;
    }

    internal static (long Length, UInt128 Md5First16k)? ReadContentKey(string path)
    {
        try
        {
            using SafeFileHandle file = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            long length = RandomAccess.GetLength(file);
            byte[] first = new byte[(int)Math.Min(length, Par2Packets.First16kLength)];
            int read = Par2FileIo.ReadUpTo(file, first, 0);
            return (length, System.Buffers.Binary.BinaryPrimitives.ReadUInt128LittleEndian(Par2Md5.Hash(first.AsSpan(0, read))));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null; // a missing or unreadable file can still match by name
        }
    }

    [GeneratedRegex(@"\.vol\d+[+-]\d+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VolumeSuffix();
}
