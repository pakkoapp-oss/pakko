namespace Archiver.Core.Recovery;

/// <summary>A recovery block found in a PAR2 file: its exponent and where its slice data starts.</summary>
internal readonly record struct Par2RecoveryBlock(uint Exponent, string Path, long DataOffset);

/// <summary>
/// A complete PAR 2.0 recovery set protecting exactly one file, every field already checked
/// against <see cref="Par2Limits"/> (T-F275). <see cref="Name"/> is only ever compared with the
/// name of a file the user chose; it never becomes a path.
/// </summary>
internal sealed class Par2Set
{
    internal required UInt128 SetId { get; init; }
    internal required long SliceSize { get; init; }
    internal required UInt128 FileId { get; init; }
    internal required UInt128 FileMd5 { get; init; }
    internal required UInt128 Md5First16k { get; init; }
    internal required long FileLength { get; init; }

    /// <summary>The FileDesc name bytes, trailing zero padding removed (UTF-8 in practice).</summary>
    internal required byte[] Name { get; init; }

    internal required Par2SliceChecksum[] Slices { get; init; }

    /// <summary>One block per exponent, in ascending exponent order.</summary>
    internal required IReadOnlyList<Par2RecoveryBlock> RecoveryBlocks { get; init; }
}

/// <summary>Why a set found in the PAR2 files cannot be used.</summary>
internal enum Par2SetProblem
{
    /// <summary>The Main, FileDesc or IFSC packet is missing or damaged in every copy.</summary>
    MissingCriticalPackets,

    /// <summary>The set protects more than one file; Pakko reads sets for a single file.</summary>
    MultipleFiles,

    /// <summary>Two valid copies of a critical packet disagree.</summary>
    ConflictingPackets,

    /// <summary>A field is outside <see cref="Par2Limits"/> or inconsistent with another.</summary>
    Malformed,
}

/// <summary>What reading a group of PAR2 files found: the usable sets and the refused ones.</summary>
internal sealed record Par2ReadResult(IReadOnlyList<Par2Set> Sets, IReadOnlyList<(UInt128 SetId, Par2SetProblem Problem)> Rejected, int UnreadableFiles);
