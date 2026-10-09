using System.Buffers.Binary;
using Microsoft.Win32.SafeHandles;

namespace Archiver.Core.Recovery;

/// <summary>
/// Reads PAR2 files as untrusted input (T-F275). A packet counts only when its length fits the
/// file, its MD5 matches and its fields pass <see cref="Par2Limits"/>; anything else is skipped and
/// the scan goes on from the next byte, so one damaged packet costs only itself. Memory is bounded:
/// only packets up to <see cref="Par2Limits.MaxSmallPacketLength"/> are read whole, and a recovery
/// block is hashed in place. Hashing per file is capped at twice its length, so a file packed with
/// fake headers cannot make the scan quadratic. Every size is <see langword="long"/> or checked.
/// </summary>
internal static class Par2PacketReader
{
    private const int WindowLength = 1 << 20;
    private const int RecoveryHeaderLength = Par2Packets.HeaderLength + Par2Packets.RecoveryPrefixLength;

    internal static Par2ReadResult Read(IEnumerable<string> paths, CancellationToken cancellationToken)
    {
        var sets = new Dictionary<UInt128, SetBuilder>();
        var candidates = new List<RecoveryCandidate>();
        int unreadable = 0;
        var files = new List<HashBudget>();
        foreach (string path in paths)
        {
            try
            {
                using SafeFileHandle file = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                var budget = new HashBudget(path, RandomAccess.GetLength(file));
                files.Add(budget);
                Scan(file, budget, sets, candidates, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                unreadable++; // the packets found before the failure still count
            }
        }

        foreach (HashBudget budget in files)
        {
            try
            {
                ValidateRecoveryBlocks(budget, candidates, sets, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                unreadable++; // the blocks validated before the failure still count
            }
        }

        var usable = new List<Par2Set>();
        var rejected = new List<(UInt128, Par2SetProblem)>();
        foreach ((UInt128 setId, SetBuilder builder) in sets)
        {
            if (builder.Build(setId, out Par2Set? set, out Par2SetProblem problem))
                usable.Add(set!);
            else
                rejected.Add((setId, problem));
        }
        return new Par2ReadResult(usable, rejected, unreadable, files.Sum(f => f.Spent));
    }

    private static void Scan(SafeFileHandle file, HashBudget budget, Dictionary<UInt128, SetBuilder> sets,
        List<RecoveryCandidate> candidates, CancellationToken cancellationToken)
    {
        string path = budget.Path;
        long fileLength = budget.FileLength;
        byte[] window = new byte[WindowLength];
        long windowStart = 0;
        int windowLength = 0;
        byte[] header = new byte[Par2Packets.HeaderLength];
        long position = 0;
        while (fileLength - position >= Par2Packets.HeaderLength)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (position < windowStart || position + Par2Packets.Magic.Length > windowStart + windowLength)
            {
                windowStart = position;
                windowLength = Par2FileIo.ReadUpTo(file, window, position);
                if (windowLength < Par2Packets.HeaderLength)
                    return;
            }
            int found = window.AsSpan((int)(position - windowStart), windowLength - (int)(position - windowStart)).IndexOf(Par2Packets.Magic);
            if (found < 0)
            {
                // The next window overlaps by the magic's length less one, so no split magic is missed.
                position = windowStart + windowLength - (Par2Packets.Magic.Length - 1);
                if (windowStart + windowLength >= fileLength)
                    return;
                windowLength = 0;
                continue;
            }
            long offset = position + found;
            position = offset + 1;
            if (Par2FileIo.ReadUpTo(file, header, offset) < header.Length)
                return;

            ulong declared = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(Par2Packets.LengthOffset));
            if (declared < Par2Packets.HeaderLength || declared % 4 != 0 || declared > (ulong)(fileLength - offset))
                continue;
            long length = (long)declared;
            UInt128 setId = BinaryPrimitives.ReadUInt128LittleEndian(header.AsSpan(Par2Packets.SetIdOffset));
            ReadOnlySpan<byte> type = header.AsSpan(Par2Packets.TypeOffset, 16);

            if (type.SequenceEqual(Par2Packets.RecoveryType))
            {
                if (length >= RecoveryHeaderLength)
                    candidates.Add(new RecoveryCandidate(path, offset, length, setId, BinaryPrimitives.ReadUInt128LittleEndian(header.AsSpan(Par2Packets.HashOffset))));
                continue;
            }
            bool critical = type.SequenceEqual(Par2Packets.MainType) || type.SequenceEqual(Par2Packets.FileDescType) || type.SequenceEqual(Par2Packets.IfscType);
            if (!critical || length > Par2Limits.MaxSmallPacketLength)
                continue;

            if (!budget.TrySpend(length))
                return;
            byte[] packet = new byte[length];
            if (Par2FileIo.ReadUpTo(file, packet, offset) < packet.Length)
                continue;
            if (!packet.AsSpan(Par2Packets.HashOffset, Par2Md5.Size).SequenceEqual(Par2Md5.Hash(packet.AsSpan(Par2Packets.SetIdOffset))))
                continue;
            if (!sets.TryGetValue(setId, out SetBuilder? builder))
                sets[setId] = builder = new SetBuilder();
            if (builder.Add(setId, type, packet.AsSpan(Par2Packets.HeaderLength)))
                position = offset + length;
        }
    }

    private static void ValidateRecoveryBlocks(HashBudget budget, List<RecoveryCandidate> candidates,
        Dictionary<UInt128, SetBuilder> sets, CancellationToken cancellationToken)
    {
        string path = budget.Path;
        long acceptedEnd = 0;
        byte[] buffer = new byte[WindowLength];
        using SafeFileHandle? file = candidates.Exists(c => c.Path == path)
            ? File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete)
            : null;
        if (file is null)
            return;
        Span<byte> exponent = stackalloc byte[4];
        foreach (RecoveryCandidate candidate in candidates.Where(c => c.Path == path).OrderBy(c => c.Offset))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (candidate.Offset < acceptedEnd
                || !sets.TryGetValue(candidate.SetId, out SetBuilder? builder)
                || builder.SliceSize is not { } sliceSize
                || candidate.Length != RecoveryHeaderLength + sliceSize)
                continue;
            long hashed = candidate.Length - Par2Packets.SetIdOffset;
            if (!budget.TrySpend(hashed))
                return;
            byte[] md5 = Par2FileIo.HashRange(file, candidate.Offset + Par2Packets.SetIdOffset, hashed, buffer);
            if (BinaryPrimitives.ReadUInt128LittleEndian(md5) != candidate.Md5)
                continue;
            if (Par2FileIo.ReadUpTo(file, exponent, candidate.Offset + Par2Packets.HeaderLength) < exponent.Length)
                continue;
            builder.AddRecovery(new Par2RecoveryBlock(BinaryPrimitives.ReadUInt32LittleEndian(exponent), path, candidate.Offset + RecoveryHeaderLength), candidate.Md5);
            acceptedEnd = candidate.Offset + candidate.Length;
        }
    }

    private readonly record struct RecoveryCandidate(string Path, long Offset, long Length, UInt128 SetId, UInt128 Md5);

    /// <summary>What one PAR2 file may cost to hash, over both phases: twice its length. A valid
    /// file needs at most its length, since its packets do not overlap.</summary>
    private sealed class HashBudget(string path, long fileLength)
    {
        private readonly long _limit = checked(2 * fileLength);

        internal string Path { get; } = path;
        internal long FileLength { get; } = fileLength;
        internal long Spent { get; private set; }

        internal bool TrySpend(long bytes)
        {
            if (bytes > _limit - Spent)
                return false;
            Spent += bytes;
            return true;
        }
    }

    /// <summary>The valid packets of one set ID, collected across files.</summary>
    private sealed class SetBuilder
    {
        private byte[]? _main;
        private readonly Dictionary<UInt128, byte[]> _fileDescs = [];
        private readonly Dictionary<UInt128, byte[]> _ifscs = [];
        private readonly Dictionary<uint, (Par2RecoveryBlock Block, UInt128 Md5)> _recovery = [];
        private readonly HashSet<uint> _conflictingExponents = [];
        private bool _conflict;

        /// <summary>The slice size of a valid Main packet, once one is seen.</summary>
        internal long? SliceSize { get; private set; }

        /// <summary>Takes a critical packet whose MD5 already matched; false when its fields make it
        /// unusable, so the scan treats it as damaged.</summary>
        internal bool Add(UInt128 setId, ReadOnlySpan<byte> type, ReadOnlySpan<byte> body)
        {
            if (type.SequenceEqual(Par2Packets.MainType))
                return AddMain(setId, body);
            if (body.Length < Par2Md5.Size)
                return false;
            UInt128 fileId = BinaryPrimitives.ReadUInt128LittleEndian(body);
            if (type.SequenceEqual(Par2Packets.FileDescType))
                return AddFileDesc(fileId, body);
            return AddOnce(_ifscs, fileId, body);
        }

        internal void AddRecovery(Par2RecoveryBlock block, UInt128 md5)
        {
            if (_conflictingExponents.Contains(block.Exponent))
                return;
            if (_recovery.TryGetValue(block.Exponent, out var existing))
            {
                if (existing.Md5 != md5)
                {
                    // Two valid blocks for one exponent: nothing says which is right, so neither is used.
                    _recovery.Remove(block.Exponent);
                    _conflictingExponents.Add(block.Exponent);
                }
                return;
            }
            _recovery[block.Exponent] = (block, md5);
        }

        // The specification defines the set ID as the MD5 of the Main body; a Main packet that is
        // not is refused, so two valid Main packets of one set cannot disagree.
        private bool AddMain(UInt128 setId, ReadOnlySpan<byte> body)
        {
            if (body.Length < 12 || (body.Length - 12) % Par2Md5.Size != 0
                || BinaryPrimitives.ReadUInt128LittleEndian(Par2Md5.Hash(body)) != setId)
                return false;
            if (_main is null)
            {
                _main = body.ToArray();
                long sliceSize = BinaryPrimitives.ReadInt64LittleEndian(body);
                if (sliceSize > 0 && sliceSize % 4 == 0 && sliceSize <= Par2Limits.MaxSliceSize)
                    SliceSize = sliceSize;
            }
            return true;
        }

        // The file ID is MD5(16 KiB MD5 ‖ length ‖ name); a FileDesc that does not match its own ID
        // is damaged or forged and is refused.
        private bool AddFileDesc(UInt128 fileId, ReadOnlySpan<byte> body)
        {
            const int NameOffset = 3 * Par2Md5.Size + 8;
            if (body.Length < NameOffset)
                return false;
            ReadOnlySpan<byte> name = body[NameOffset..].TrimEnd((byte)0);
            ulong length = BinaryPrimitives.ReadUInt64LittleEndian(body[(3 * Par2Md5.Size)..]);
            if (length > long.MaxValue)
                return false;
            byte[] expected = Par2Packets.FileId(body.Slice(2 * Par2Md5.Size, Par2Md5.Size), (long)length, name);
            if (BinaryPrimitives.ReadUInt128LittleEndian(expected) != fileId)
                return false;
            return AddOnce(_fileDescs, fileId, body);
        }

        private bool AddOnce(Dictionary<UInt128, byte[]> packets, UInt128 fileId, ReadOnlySpan<byte> body)
        {
            if (packets.TryGetValue(fileId, out byte[]? existing))
            {
                if (!existing.AsSpan().SequenceEqual(body))
                    _conflict = true;
                return true;
            }
            packets[fileId] = body.ToArray();
            return true;
        }

        internal bool Build(UInt128 setId, out Par2Set? set, out Par2SetProblem problem)
        {
            set = null;
            problem = Par2SetProblem.MissingCriticalPackets;
            if (_main is null)
                return false;
            uint recoveryFiles = BinaryPrimitives.ReadUInt32LittleEndian(_main.AsSpan(8));
            if (recoveryFiles != 1 || _main.Length != 12 + Par2Md5.Size)
            {
                problem = Par2SetProblem.MultipleFiles;
                return false;
            }
            if (SliceSize is not { } sliceSize)
            {
                problem = Par2SetProblem.Malformed;
                return false;
            }
            UInt128 fileId = BinaryPrimitives.ReadUInt128LittleEndian(_main.AsSpan(12));
            if (_conflict)
            {
                problem = Par2SetProblem.ConflictingPackets;
                return false;
            }
            if (!_fileDescs.TryGetValue(fileId, out byte[]? desc))
                return false;
            long fileLength = BinaryPrimitives.ReadInt64LittleEndian(desc.AsSpan(3 * Par2Md5.Size));
            long sliceCount = Par2FileHasher.SliceCount(fileLength, sliceSize);
            if (sliceCount > Par2Limits.MaxInputSlices)
            {
                problem = Par2SetProblem.Malformed;
                return false;
            }
            Par2SliceChecksum[] slices = [];
            if (sliceCount > 0)
            {
                if (!_ifscs.TryGetValue(fileId, out byte[]? ifsc))
                    return false;
                if (ifsc.Length != Par2Md5.Size + sliceCount * Par2SliceChecksum.Length)
                {
                    problem = Par2SetProblem.Malformed;
                    return false;
                }
                slices = new Par2SliceChecksum[sliceCount];
                for (int i = 0; i < slices.Length; i++)
                    slices[i] = Par2SliceChecksum.Read(ifsc.AsSpan(Par2Md5.Size + i * Par2SliceChecksum.Length));
            }

            set = new Par2Set
            {
                SetId = setId,
                SliceSize = sliceSize,
                FileId = fileId,
                FileMd5 = BinaryPrimitives.ReadUInt128LittleEndian(desc.AsSpan(Par2Md5.Size)),
                Md5First16k = BinaryPrimitives.ReadUInt128LittleEndian(desc.AsSpan(2 * Par2Md5.Size)),
                FileLength = fileLength,
                Name = desc.AsSpan(3 * Par2Md5.Size + 8).TrimEnd((byte)0).ToArray(),
                Slices = slices,
                RecoveryBlocks = [.. _recovery.Values.Select(v => v.Block).OrderBy(b => b.Exponent)],
            };
            return true;
        }
    }
}
