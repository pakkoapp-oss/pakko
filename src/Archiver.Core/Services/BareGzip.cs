using System.Buffers.Binary;
using System.IO.Compression;
using Archiver.Core.IO;

namespace Archiver.Core.Services;

/// <summary>
/// T-F413: a gzip stream holding one file, not an archive (7-Zip's -tgzip, gzip(1) itself).
/// tar.exe cannot read one: bsdtar enables every libarchive reader but raw (measured on 3.8.9:
/// "Unrecognized archive format"). Pakko reads it with .NET's DeflateStream, the
/// System.IO.Compression it uses for ZIP, and checks every member's trailer itself: DeflateStream
/// and GZipStream end without a word on a stream cut before its trailer or followed by other
/// bytes (measured), so a test would pass a damaged file.
/// </summary>
internal static class BareGzip
{
    private const int TarBlock = 512;
    private const int CopyBuffer = 81920;
    private const int TrailerLength = 8;
    // The head is read across members (an empty member may come first, as bsdtar reads past it),
    // with at most this much compressed input fed a byte at a time; past it the stream stays on
    // the tar route undecided.
    private const int HeadInputLimit = 1024 * 1024;
    private const byte FlagHeaderCrc = 0x02, FlagExtra = 0x04, FlagName = 0x08, FlagComment = 0x10, FlagReserved = 0xE0;

    /// <summary>What reading the stream found.</summary>
    /// <param name="Length">The bytes it decompressed to, all members.</param>
    /// <param name="Damaged">A header, the compressed data or a trailer is wrong, a member is cut
    /// short, or other bytes follow the last member.</param>
    /// <param name="ModifiedUtc">The first member's MTIME, or null when it is 0 ("not known").</param>
    internal readonly record struct ReadResult(long Length, bool Damaged, DateTime? ModifiedUtc);

    /// <summary>
    /// True when <paramref name="path"/> is a gzip stream whose start decompresses to nothing
    /// tar.exe opens: no tar header and no signature of another format libarchive reads (cpio and
    /// zip inside gzip list today, measured). False on any read problem, so the tar route reports
    /// it as before.
    /// </summary>
    public static bool IsBareGzip(string path)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (ReadHeader(file) is null)
                return false;
            byte[] head = new byte[TarBlock];
            int? length = ReadHead(file, head);
            return length is { } read && !OpensAsAnArchive(head.AsSpan(0, read));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
            or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    // As much of the start as decompresses, member after member (no trailer is checked here): a
    // stream damaged further on is still one file, and the engine reports the damage. Null when not
    // one byte decompresses before the damage, or the head needs more input than HeadInputLimit -
    // the tar route reports such a file as before. The file is past the first member's header.
    private static int? ReadHead(FileStream file, byte[] head)
    {
        int length = 0;
        long fed = 0;
        while (true)
        {
            long dataStart = file.Position;
            var input = new FeedStream(file, fastBytes: 0, HeadInputLimit - fed);
            bool memberEnded;
            try
            {
                using var inflate = new DeflateStream(input, CompressionMode.Decompress, leaveOpen: true);
                int read;
                while (length < head.Length && (read = inflate.Read(head, length, head.Length - length)) > 0)
                    length += read;
                memberEnded = length < head.Length;
            }
            catch (InvalidDataException)
            {
                return length > 0 ? length : null;
            }
            fed += input.BytesRead;
            if (input.LimitReached)
                return null;
            if (!memberEnded)
                return length;
            file.Position = dataStart + input.BytesRead + TrailerLength;
            if (file.Position >= file.Length || ReadHeader(file) is null)
                return length;
        }
    }

    // What tar.exe reads inside a gzip: a tar (a header whose checksum holds, or the zero block of
    // an empty one), or a format or compressed stream libarchive knows by its first bytes.
    internal static bool OpensAsAnArchive(ReadOnlySpan<byte> head)
    {
        if (head.Length == TarBlock && (IsTarHeader(head) || !head.ContainsAnyExcept((byte)0)))
            return true;
        foreach (byte[] signature in Signatures)
        {
            if (head.StartsWith(signature))
                return true;
        }
        // LHA: "-lh?-" or "-lz?-" after the header's size and checksum bytes.
        return head.Length >= 7 && head[2] == '-' && head[3] == 'l' && head[6] == '-';
    }

    private static readonly byte[][] Signatures =
    [
        "PK\u0003\u0004"u8.ToArray(), "PK\u0005\u0006"u8.ToArray(), "PK\u0007\u0008"u8.ToArray(), // zip
        "070707"u8.ToArray(), "070701"u8.ToArray(), "070702"u8.ToArray(), [0xC7, 0x71], [0x71, 0xC7], // cpio
        "!<arch>\n"u8.ToArray(),                                   // ar
        [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C],                       // 7z
        "Rar!\u001A\u0007"u8.ToArray(),                            // rar
        "MSCF"u8.ToArray(), "xar!"u8.ToArray(), "#mtree"u8.ToArray(), "WARC/"u8.ToArray(),
        [0xED, 0xAB, 0xEE, 0xDB],                                  // rpm
        [0x1F, 0x8B], [0x1F, 0x9D], "BZh"u8.ToArray(), [0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00], // gzip, compress, bzip2, xz
        [0x28, 0xB5, 0x2F, 0xFD], "LZIP"u8.ToArray(),              // zstd, lzip
    ];

    // POSIX tar: the header's bytes summed with its checksum field read as spaces, against the
    // octal number in that field (libarchive's own test, archive_read_support_format_tar.c).
    private static bool IsTarHeader(ReadOnlySpan<byte> block)
    {
        ReadOnlySpan<byte> field = block.Slice(148, 8);
        long stored = 0;
        bool digits = false;
        foreach (byte b in field)
        {
            if (b is >= (byte)'0' and <= (byte)'7')
            {
                stored = (stored * 8) + (b - '0');
                digits = true;
            }
            else if (b is not (0 or (byte)' '))
            {
                return false;
            }
        }
        if (!digits)
            return false;
        // Old tars summed signed chars; libarchive takes either sum.
        long sum = 8 * ' ', signedSum = 8 * ' ';
        for (int i = 0; i < TarBlock; i++)
        {
            if (i < 148 || i >= 156)
            {
                sum += block[i];
                signedSum += (sbyte)block[i];
            }
        }
        return sum == stored || signedSum == stored;
    }

    /// <summary>
    /// The name 7-Zip gives the file inside (<c>DefaultName.cpp</c>, GetDefaultName2, with the gzip
    /// handler's extensions "gz gzip tgz tpz apk" -> "", "", ".tar", ".tar", ".tar"): the archive's
    /// name without that extension, else without its last dot part, else the name with "~" added.
    /// The header's own FNAME is not used (7-Zip reads it as a path; see DECISIONS.md T-F413).
    /// </summary>
    public static string OutputName(string archivePath)
    {
        string name = Path.GetFileName(archivePath);
        foreach ((string extension, string added) in Extensions)
        {
            if (name.Length > extension.Length + 1 && name[^(extension.Length + 1)] == '.'
                && name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                return Usable(name[..^(extension.Length + 1)] + added);
            }
        }
        int dot = name.LastIndexOf('.');
        return Usable(dot > 0 ? name[..dot] : name + "~");
    }

    // A name Windows writes as itself: "...gz" would be "..", " .gz" empty - the destination's
    // parent or the destination itself. Trailing dots and spaces go, as Windows drops them; a name
    // left empty is '_' (7-Zip's k_EmptyReplaceName, as EntryDiskName).
    private static string Usable(string name)
    {
        string trimmed = name.TrimEnd(' ', '.');
        return trimmed.Length == 0 ? "_" : trimmed;
    }

    private static readonly (string Extension, string Added)[] Extensions =
        [("gz", ""), ("gzip", ""), ("tgz", ".tar"), ("tpz", ".tar"), ("apk", ".tar")];

    /// <summary>
    /// Decompresses every member of <paramref name="path"/> into <paramref name="destination"/>
    /// (or nowhere, to test or measure it) and checks each member's CRC-32 and size against its
    /// trailer. Past <paramref name="limit"/> decompressed bytes it stops as damaged (the file
    /// changed since it was measured). Throws <see cref="IOException"/> or
    /// <see cref="UnauthorizedAccessException"/> when a file cannot be read or written,
    /// <see cref="OperationCanceledException"/> on cancel.
    /// </summary>
    public static ReadResult Read(string path, Stream? destination, CancellationToken cancellationToken, long limit = long.MaxValue)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
        long total = 0;
        DateTime? modified = null;
        long position = 0;
        bool first = true;
        byte[] buffer = new byte[CopyBuffer];
        do
        {
            file.Position = position;
            if (ReadHeader(file) is not { } mtime)
                return new ReadResult(total, Damaged: true, modified);
            if (first && mtime != 0)
                modified = DateTime.UnixEpoch.AddSeconds(mtime);
            first = false;

            long dataStart = file.Position;
            var counted = new CountingStream(file);
            var crc = new Crc32.Accumulator();
            long memberLength = 0;
            try
            {
                using var inflate = new DeflateStream(counted, CompressionMode.Decompress, leaveOpen: true);
                int read;
                while ((read = inflate.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (read > limit - total - memberLength)
                        return new ReadResult(total + memberLength, Damaged: true, modified);
                    crc.Update(buffer.AsSpan(0, read));
                    destination?.Write(buffer, 0, read);
                    memberLength += read;
                }
            }
            catch (InvalidDataException)
            {
                return new ReadResult(total + memberLength, Damaged: true, modified);
            }
            total += memberLength;

            long dataEnd = ExactDataEnd(file, dataStart, counted.BytesRead - counted.LastRead, buffer, cancellationToken);
            if (!TrailerIsAt(file, dataEnd, crc.Finish(), (uint)memberLength))
                return new ReadResult(total, Damaged: true, modified);
            position = dataEnd + TrailerLength;
        }
        while (position < file.Length);
        return new ReadResult(total, Damaged: false, modified);
    }

    // The fixed header and its optional fields (RFC 1952, 2.3); null when it is not a gzip
    // member header or ends early. Returns the member's MTIME.
    private static uint? ReadHeader(FileStream file)
    {
        long headerStart = file.Position;
        Span<byte> fixedPart = stackalloc byte[10];
        if (file.ReadAtLeast(fixedPart, 10, throwOnEndOfStream: false) < 10
            || fixedPart[0] != 0x1F || fixedPart[1] != 0x8B || fixedPart[2] != 8 || (fixedPart[3] & FlagReserved) != 0)
        {
            return null;
        }
        byte flags = fixedPart[3];
        if ((flags & FlagExtra) != 0)
        {
            Span<byte> size = stackalloc byte[2];
            if (file.ReadAtLeast(size, 2, throwOnEndOfStream: false) < 2)
                return null;
            file.Position += BinaryPrimitives.ReadUInt16LittleEndian(size);
        }
        if ((flags & FlagName) != 0 && !SkipZeroTerminated(file))
            return null;
        if ((flags & FlagComment) != 0 && !SkipZeroTerminated(file))
            return null;
        if ((flags & FlagHeaderCrc) != 0 && !HeaderCrcHolds(file, headerStart))
            return null;
        return file.Position <= file.Length ? BinaryPrimitives.ReadUInt32LittleEndian(fixedPart[4..8]) : null;
    }

    // FHCRC: the low 16 bits of the CRC-32 of every header byte before it (RFC 1952, 2.3.1).
    private static bool HeaderCrcHolds(FileStream file, long headerStart)
    {
        long crcAt = file.Position;
        if (crcAt + 2 > file.Length)
            return false;
        var crc = new Crc32.Accumulator();
        byte[] chunk = new byte[4096];
        file.Position = headerStart;
        for (long left = crcAt - headerStart; left > 0;)
        {
            int read = file.Read(chunk, 0, (int)Math.Min(chunk.Length, left));
            if (read <= 0)
                return false;
            crc.Update(chunk.AsSpan(0, read));
            left -= read;
        }
        Span<byte> stored = stackalloc byte[2];
        file.ReadExactly(stored);
        return BinaryPrimitives.ReadUInt16LittleEndian(stored) == (ushort)crc.Finish();
    }

    private static bool SkipZeroTerminated(FileStream file)
    {
        int b;
        while ((b = file.ReadByte()) > 0)
        {
            // only skipping: FNAME and FCOMMENT are never shown or used
        }
        return b == 0;
    }

    // Where the member's compressed data ends, to the byte. DeflateStream reads its input in
    // blocks and tells nothing of what it left unread (measured: a seekable stream stays at the
    // block's end), so the member is inflated again with the input fed a byte at a time from the
    // block in which the first pass ended - the end lies in that block. Costs a second inflate.
    private static long ExactDataEnd(FileStream file, long dataStart, long lastBlockStart, byte[] buffer, CancellationToken cancellationToken)
    {
        file.Position = dataStart;
        var input = new FeedStream(file, lastBlockStart, long.MaxValue);
        using (var inflate = new DeflateStream(input, CompressionMode.Decompress, leaveOpen: true))
        {
            while (inflate.Read(buffer, 0, buffer.Length) > 0)
                cancellationToken.ThrowIfCancellationRequested();
        }
        return dataStart + input.BytesRead;
    }

    // The 8-byte trailer (CRC-32, size mod 2^32) right where the compressed data ends.
    private static bool TrailerIsAt(FileStream file, long at, uint crc, uint size)
    {
        if (at + TrailerLength > file.Length)
            return false;
        Span<byte> stored = stackalloc byte[TrailerLength];
        file.Position = at;
        file.ReadExactly(stored);
        return BinaryPrimitives.ReadUInt32LittleEndian(stored) == crc && BinaryPrimitives.ReadUInt32LittleEndian(stored[4..]) == size;
    }

    // Counts what DeflateStream took from the file and the size of its last read.
    private sealed class CountingStream(Stream inner) : Stream
    {
        public long BytesRead { get; private set; }
        public int LastRead { get; private set; }

        public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));

        public override int Read(Span<byte> buffer) => Count(inner.Read(buffer));

        private int Count(int read)
        {
            if (read > 0)
            {
                LastRead = read;
                BytesRead += read;
            }
            return read;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { /* read-only */ }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    // Gives DeflateStream its input as it asks up to fastBytes, then one byte a read, so the bytes
    // it took are exactly what it decoded; stops (as the end of input) at limit bytes.
    private sealed class FeedStream(Stream inner, long fastBytes, long limit) : Stream
    {
        public long BytesRead { get; private set; }
        public bool LimitReached => BytesRead >= limit;

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (buffer.IsEmpty || LimitReached)
                return 0;
            long allowed = BytesRead < fastBytes ? fastBytes - BytesRead : 1;
            int read = inner.Read(buffer[..(int)Math.Min(buffer.Length, Math.Min(allowed, limit - BytesRead))]);
            BytesRead += read;
            return read;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { /* read-only */ }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
