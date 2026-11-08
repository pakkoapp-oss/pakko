using System.Globalization;
using System.Text;
using Archiver.Core.Models;

namespace Archiver.Core.Services.Sandbox;

/// <summary>What a helper run was asked to do; it decides which lines may appear.</summary>
internal enum ArchiveHelperMode
{
    Probe,
    List,
    Test,
    Extract,
}

/// <summary>The helper's exit codes (docs/ARCHITECTURE.md, "Archive helper").</summary>
internal enum ArchiveHelperExit
{
    Done = 0,
    Archive = 1,
    Usage = 2,
    Load = 3,
    Export = 4,
    Selection = 5,
    Write = 6,
    Capability = 7,
}

/// <summary>One "E" line: an entry as libarchive read it.</summary>
/// <param name="Index">Its position in the archive, from 0.</param>
/// <param name="FileType">archive_entry_filetype; 0 for a hard link.</param>
/// <param name="Size">Null when the entry has no size.</param>
/// <param name="MtimeSeconds">Null when the entry has no time.</param>
/// <param name="MtimeNanoseconds">0 when the entry has no time.</param>
/// <param name="Encrypted">Null when libarchive cannot tell.</param>
/// <param name="SparseCount">How many data runs a sparse entry has; 0 for a plain one.</param>
/// <param name="Name">Exact UTF-16; null when libarchive could not convert it.</param>
/// <param name="Link">The symlink target, else the hard-link target; null for neither.</param>
internal sealed record ArchiveHelperEntry(
    long Index, int FileType, long? Size, long? MtimeSeconds, int MtimeNanoseconds, bool? Encrypted, int SparseCount, string? Name, string? Link);

/// <summary>An "F" line: a libarchive status with its error string, or one of the helper's own codes.</summary>
internal sealed record ArchiveHelperFailure(long Code, string? Message);

/// <summary>
/// T-F470: the strict reader of one helper run's stdout. The helper is treated as compromisable, so
/// every line is checked against the protocol (docs/ARCHITECTURE.md, "Archive helper") before any
/// value is used, and anything else - a byte outside printable ASCII, a field out of range, a line out
/// of order, an index that skips, more entries than the limit - throws
/// <see cref="ArchiveHelperProtocolException"/>. Fed one line at a time, as the helper writes them.
/// </summary>
internal sealed class ArchiveHelperOutput
{
    /// <summary>Entries one run may list; the helper's own selection limit.</summary>
    internal const int MaxEntries = 4_000_000;

    /// <summary>
    /// The largest size an entry may declare or a read may report: the sum over <see cref="MaxEntries"/>
    /// entries still fits a long (about 2 TiB each).
    /// </summary>
    internal const long MaxEntrySize = long.MaxValue / MaxEntries;

    /// <summary>A name or link target in UTF-16 code units: the longest Windows path.</summary>
    internal const int MaxNameUnits = 32_767;

    /// <summary>The longest line the protocol allows: an "E" line with two names at the limit.</summary>
    internal const int MaxLineBytes = 2 * 4 * MaxNameUnits + 256;

    private const int MaxMessageBytes = 4096;
    private const int MaxExportNameLength = 64;
    private const int MaxVersionBytes = 1024;

    private static readonly string[] SupportNames =
        ["tar", "cpio", "7zip", "rar", "rar5", "cab", "gzip", "bzip2", "compress", "xz", "lzma", "lzip", "zstd"];

    private readonly ArchiveHelperMode _mode;
    private readonly List<ArchiveHelperEntry> _entries = [];
    private readonly HashSet<long> _warned = [];
    private readonly List<string> _missingExports = [];
    private readonly Dictionary<string, int?> _capabilities = new(StringComparer.Ordinal);
    private readonly Action<long, long>? _onRead;
    private int _lines;
    private long _lastRead = -1;
    private bool _versionSeen;
    private bool _pastPreamble;
    private bool _ended;

    /// <param name="mode">The mode the helper ran in.</param>
    /// <param name="onRead">Called for each "K" line with the entry's index and its bytes, as it arrives.</param>
    public ArchiveHelperOutput(ArchiveHelperMode mode, Action<long, long>? onRead = null)
    {
        _mode = mode;
        _onRead = onRead;
    }

    /// <summary>archive_version_details(), when the DLL loaded and has it.</summary>
    public string? Version { get; private set; }

    /// <summary>Core exports the DLL lacks ("X" lines).</summary>
    public IReadOnlyList<string> MissingExports => _missingExports;

    /// <summary>Probe: each support_* call's return, null where the DLL lacks the export.</summary>
    public IReadOnlyDictionary<string, int?> Capabilities => _capabilities;

    /// <summary>The entries in archive order; an entry's index is its position.</summary>
    public IReadOnlyList<ArchiveHelperEntry> Entries => _entries;

    /// <summary>Indices whose header came with ARCHIVE_WARN.</summary>
    public IReadOnlySet<long> Warned => _warned;

    /// <summary>The selected entries read in full ("K"), index to bytes, in order.</summary>
    public IReadOnlyList<(long Index, long Bytes)> Read => _read;

    private readonly List<(long Index, long Bytes)> _read = [];

    public ArchiveHelperFailure? Failure { get; private set; }

    public bool Done { get; private set; }

    /// <summary>Checks and takes one line, without its '\n'.</summary>
    public void Feed(ReadOnlySpan<byte> line)
    {
        if (_ended)
            throw Broken("a line after the run's last one");
        if (line.Length == 0 || line.Length > MaxLineBytes)
            throw Broken("an empty or overlong line");
        if (line.ContainsAnyExceptInRange((byte)0x20, (byte)0x7E))
            throw Broken("a byte outside printable ASCII");

        string[] fields = Encoding.ASCII.GetString(line).Split(' ');
        if (TakePreamble(fields, first: _lines++ == 0))
            return;
        if (!_versionSeen)
            throw Broken("no version line first");
        _pastPreamble = true;
        TakeBody(fields);
    }

    // "V" first, then any "X": true when the line was one of them.
    private bool TakePreamble(string[] fields, bool first)
    {
        switch (fields[0])
        {
            case "V" when first && fields.Length == 2:
                _versionSeen = true;
                Version = Hex8(fields[1], MaxVersionBytes);
                return true;
            case "X" when _versionSeen && !_pastPreamble && fields.Length == 2:
                _missingExports.Add(ExportName(fields[1]));
                return true;
            default:
                return false;
        }
    }

    private void TakeBody(string[] fields)
    {
        switch (fields[0])
        {
            case "C" when _mode == ArchiveHelperMode.Probe && fields.Length == 3:
                TakeCapability(fields[1], fields[2]);
                break;
            case "W" when _mode != ArchiveHelperMode.Probe && fields.Length == 4:
                TakeWarning(fields);
                break;
            case "E" when _mode != ArchiveHelperMode.Probe && fields.Length == 10:
                TakeEntry(fields);
                break;
            case "K" when _mode is ArchiveHelperMode.Test or ArchiveHelperMode.Extract && fields.Length == 3:
                TakeRead(fields);
                break;
            case "F" when fields.Length == 3:
                Failure = new ArchiveHelperFailure(Signed(fields[1], long.MinValue + 1, long.MaxValue), Hex8(fields[2], MaxMessageBytes));
                _ended = true;
                break;
            case "D" when fields.Length == 1:
                if (_mode == ArchiveHelperMode.Probe && _capabilities.Count != SupportNames.Length)
                    throw Broken("a probe that did not report every support call");
                Done = true;
                _ended = true;
                break;
            default:
                throw Broken("a line the protocol does not have here");
        }
    }

    /// <summary>
    /// After the helper exited: its exit code must agree with how its output ended (a "D" only with 0,
    /// an "F" only with 1, 5 or 7). Returns the exit code as named.
    /// </summary>
    public ArchiveHelperExit Finish(int exitCode)
    {
        if (exitCode is < 0 or > 7)
            throw Broken("an exit code the helper does not have");
        var exit = (ArchiveHelperExit)exitCode;
        bool agrees = exit switch
        {
            ArchiveHelperExit.Done => Done,
            ArchiveHelperExit.Archive => Failure is { Code: not 0 },
            ArchiveHelperExit.Selection => !Done && (Failure is null || Failure.Code == -6),
            ArchiveHelperExit.Capability => Failure is { Code: 0 },
            ArchiveHelperExit.Usage or ArchiveHelperExit.Load => _lines == 0,
            _ => !Done && Failure is null,
        };
        if (!agrees)
            throw Broken($"exit code {exitCode} against its output");
        return exit;
    }

    private void TakeCapability(string name, string value)
    {
        if (_capabilities.Count >= SupportNames.Length || SupportNames[_capabilities.Count] != name)
            throw Broken("a support call out of order");
        _capabilities[name] = value == "-" ? null : (int)Signed(value, int.MinValue, int.MaxValue);
    }

    private void TakeWarning(string[] fields)
    {
        long index = Unsigned(fields[1], MaxEntries - 1);
        if (index != _entries.Count || !_warned.Add(index))
            throw Broken("a warning not for the next entry");
        _ = Signed(fields[2], int.MinValue, int.MaxValue);
        _ = Hex8(fields[3], MaxMessageBytes);
    }

    private void TakeEntry(string[] f)
    {
        long index = Unsigned(f[1], MaxEntries - 1);
        if (index != _entries.Count)
            throw Broken("an entry index out of sequence");
        int fileType = (int)Unsigned(f[2], ushort.MaxValue);
        long? size = f[3] == "-" ? null : Unsigned(f[3], MaxEntrySize);
        long? mtime = f[4] == "-" ? null : Signed(f[4], long.MinValue + 1, long.MaxValue);
        int nanoseconds = (int)Unsigned(f[5], 999_999_999);
        if (mtime is null && nanoseconds != 0)
            throw Broken("nanoseconds without a time");
        bool? encrypted = f[6] switch
        {
            "-" => null,
            "0" => false,
            "1" => true,
            _ => throw Broken("an encryption flag that is not 0, 1 or -"),
        };
        int sparse = (int)Unsigned(f[7], int.MaxValue);
        _entries.Add(new ArchiveHelperEntry(index, fileType, size, mtime, nanoseconds, encrypted, sparse, Hex16(f[8]), Hex16(f[9])));
    }

    private void TakeRead(string[] fields)
    {
        long index = Unsigned(fields[1], MaxEntries - 1);
        if (index >= _entries.Count || index <= _lastRead)
            throw Broken("a read entry not listed or out of order");
        long bytes = Unsigned(fields[2], MaxEntrySize);
        _lastRead = index;
        _read.Add((index, bytes));
        _onRead?.Invoke(index, bytes);
    }

    // Decimal, no sign, no leading zero but "0" itself, at most max.
    private static long Unsigned(string s, long max)
    {
        if (s.Length is 0 or > 19 || (s.Length > 1 && s[0] == '0'))
            throw Broken("a malformed number");
        long value = 0;
        foreach (char c in s)
        {
            if (c is < '0' or > '9')
                throw Broken("a malformed number");
            int digit = c - '0';
            if (value > (max - digit) / 10)
                throw Broken("a number out of range");
            value = value * 10 + digit;
        }
        return value;
    }

    private static long Signed(string s, long min, long max)
    {
        if (s.Length > 1 && s[0] == '-' && s[1] != '0')
        {
            long magnitude = Unsigned(s[1..], long.MaxValue);
            return -magnitude >= min ? -magnitude : throw Broken("a number out of range");
        }
        return Unsigned(s, max);
    }

    // "-" null, "." empty, else uppercase hex, two digits per byte; the bytes are read as Latin-1.
    private static string? Hex8(string s, int maxBytes)
    {
        if (s == "-")
            return null;
        if (s == ".")
            return string.Empty;
        if (s.Length % 2 != 0 || s.Length / 2 > maxBytes || !IsUpperHex(s))
            throw Broken("malformed hex text");
        return Encoding.Latin1.GetString(Convert.FromHexString(s));
    }

    // "-" null, "." empty, else uppercase hex, four digits per UTF-16 code unit. A lone surrogate is
    // kept as it is: the reader refuses such a name (HelperReader.FirstUnsafeName), the parser only reads it.
    private static string? Hex16(string s)
    {
        if (s == "-")
            return null;
        if (s == ".")
            return string.Empty;
        if (s.Length % 4 != 0 || s.Length / 4 > MaxNameUnits || !IsUpperHex(s))
            throw Broken("a malformed name");
        return string.Create(s.Length / 4, s, static (chars, hex) =>
        {
            for (int i = 0; i < chars.Length; i++)
                chars[i] = (char)ushort.Parse(hex.AsSpan(i * 4, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        });
    }

    private static bool IsUpperHex(string s) => s.All(char.IsAsciiHexDigitUpper);

    private static string ExportName(string s)
    {
        if (s.Length is 0 or > MaxExportNameLength || s.Any(c => c is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '_')))
            throw Broken("a malformed export name");
        return s;
    }

    private static ArchiveHelperProtocolException Broken(string what) => new(what);
}

/// <summary>
/// The helper's stdout broke its protocol: Pakko trusts nothing it said, and the operation fails.
/// </summary>
internal sealed class ArchiveHelperProtocolException(string what) // NOSONAR: S3871 — internal, always caught and turned into an ArchiveError, as CoreTextIOException is
    : IOException(CoreMessages.Text(MessageCode.CannotReadArchive, "the archive helper's output broke its protocol (" + what + ")").English), ICoreTextSource
{
    public CoreText Text { get; } = CoreMessages.Text(MessageCode.CannotReadArchive, "the archive helper's output broke its protocol (" + what + ")");
}
