using System.IO.Compression;

namespace Archiver.Core.Models;

public sealed record ArchiveOptions
{
    public IReadOnlyList<string> SourcePaths { get; init; } = [];

    /// <summary>
    /// T-F414: for each of <see cref="SourcePaths"/>, in the same order, how many of the last parts
    /// of its path name it in the archive: 1 is its own name, 2 keeps its parent folder
    /// (<c>C:\w\dir\sub</c> becomes <c>dir/sub/...</c>, 7-Zip's path as typed). Null names every
    /// source after itself. A count the path cannot give, or one source landing inside another's
    /// name, fails the call before anything is written.
    /// </summary>
    public IReadOnlyList<int>? SourceNameParts { get; init; }

    /// <summary>
    /// T-F410: asked for each entry with its name in the archive ('/' between parts, no trailing
    /// one) and whether it is a folder; false leaves a file out, or a folder with all it holds,
    /// a source itself included (7-Zip's <c>a -x</c>). Nothing is reported for it. Null keeps all.
    /// A source renamed for a clash (T-F30, T-F171) is asked about by its name before the rename.
    /// </summary>
    public Func<string, bool, bool>? EntryFilter { get; init; }

    public string DestinationFolder { get; init; } = string.Empty;
    /// <summary>Null auto-names the archive from <see cref="SourcePaths"/> (see ArchiveNaming.ResolveSingleArchiveName).</summary>
    public string? ArchiveName { get; init; }

    /// <summary>
    /// T-F221: a single archive's whole file name, used as given with no extension added (7-Zip's
    /// rule for a name the user typed); overrides <see cref="ArchiveName"/>. Null names it from
    /// ArchiveName plus the format's extension. Ignored by <see cref="ArchiveMode.SeparateArchives"/>.
    /// </summary>
    public string? ExactFileName { get; init; }
    public ArchiveMode Mode { get; init; } = ArchiveMode.SingleArchive;
    public ConflictBehavior OnConflict { get; init; } = ConflictBehavior.Skip;
    public bool OpenDestinationFolder { get; init; } = false;
    public CompressionLevel CompressionLevel { get; init; } = CompressionLevel.Optimal;
    public ArchiveContainerFormat Format { get; init; } = ArchiveContainerFormat.Zip;

    /// <summary>
    /// T-F342: the compression method of a <see cref="ArchiveContainerFormat.SevenZip"/> archive;
    /// other formats ignore it. <see cref="CompressionLevel"/> sets its level (Fastest 1, Optimal 6,
    /// SmallestSize 9); NoCompression stores, whatever the method.
    /// </summary>
    public SevenZipMethod SevenZipMethod { get; init; } = SevenZipMethod.Lzma2;

    /// <summary>
    /// T-F411: at most this many files are compressed at once by the ZIP engine (7-Zip's -mmt),
    /// up to 16; 0 or less follows the core count. The archive's bytes do not depend on it. The
    /// tar-family formats and 7z are written by tar.exe, which this does not reach.
    /// </summary>
    public int MaxThreads { get; init; }

    /// <summary>
    /// T-F411: the archive takes the newest modification time among the files and folders it
    /// holds, as read from disk (7-Zip's -stl), instead of the time it was written. A time that
    /// cannot be set leaves the written one. Ignored by <see cref="ArchiveMode.SeparateArchives"/>.
    /// </summary>
    public bool SetArchiveTimeFromNewestEntry { get; init; }

    /// <summary>
    /// T-F411: the ZIP engine also reads a file another program holds open for writing (7-Zip's
    /// -ssw); without it such a file is an error, as in 7-Zip. Each entry's size and CRC come from
    /// the bytes read, so the archive stays consistent; a stored entry read twice fails the
    /// archive if the file changed in between. tar.exe reads such files without being asked.
    /// </summary>
    public bool ReadFilesOpenForWriting { get; init; }

    /// <summary>
    /// T-F06: invoked once per conflicting destination path when <see cref="OnConflict"/> is
    /// <see cref="ConflictBehavior.Ask"/>. Null (e.g. Archiver.Shell, or a test that doesn't wire
    /// it) falls back to Skip — see ConflictResolver.
    /// </summary>
    public Func<ConflictInfo, Task<ConflictDecision>>? ResolveConflictAsync { get; init; }

    /// <summary>
    /// T-F193: when set, the ZIP is password-protected with WinZip AES-256 (AE-2) — every file
    /// entry, never directory entries. Invoked once per call, before any destination conflict is
    /// resolved, with <see cref="PasswordPurpose.Encrypt"/>. A cancelled (null), empty, non-ASCII
    /// or longer-than-99-character answer fails the call without creating anything (7-Zip's own
    /// creation rule — it cannot open anything else). Tar-family formats and 7z have no encrypting
    /// writer: setting this for them is an error, never a silently unencrypted archive.
    /// </summary>
    public Func<PasswordPromptInfo, Task<PasswordDecision>>? ResolvePasswordAsync { get; init; }

    /// <summary>
    /// T-F275: PAR2 recovery data written next to each created archive, as a percent of its
    /// slices (1-100); 0 writes none. It is computed over the finished archive's bytes, so an
    /// encrypted ZIP is protected as ciphertext. Refused under the DisableRecoveryData policy.
    /// </summary>
    public int RecoveryPercent { get; init; }
}

/// <summary>Whether multiple source items produce one archive or one archive each.</summary>
public enum ArchiveMode
{
    /// <summary>All sources go into a single archive.</summary>
    SingleArchive,

    /// <summary>Each source item gets its own archive.</summary>
    SeparateArchives
}

/// <summary>How a destination conflict is resolved.</summary>
public enum ConflictBehavior
{
    /// <summary>Overwrite the existing item.</summary>
    Overwrite,

    /// <summary>Leave the existing item untouched.</summary>
    Skip,

    /// <summary>
    /// On extraction: renames per-file inside a merged existing folder — this does NOT mean
    /// "always create a fresh whole folder." For shell-only "always fresh" behavior, use
    /// ExtractOptions.SeparateFolderName instead.
    /// </summary>
    Rename,

    /// <summary>Ask the caller via <see cref="ArchiveOptions.ResolveConflictAsync"/>/ExtractOptions.ResolveConflictAsync.</summary>
    Ask
}
