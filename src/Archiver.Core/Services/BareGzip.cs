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
    // DeflateStream reads its input 8 KiB at a time, so it stops at most that far past the end
    // of the compressed data; the trailer is looked for in this much before where it stopped.
    private const int TrailerWindow = 64 * 1024;
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
            Span<byte> magic = stackalloc byte[2];
            if (file.ReadAtLeast(magic, 2, throwOnEndOfStream: false) < 2 || magic[0] != 0x1F || magic[1] != 0x8B)
                return false;
            file.Position = 0;
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            byte[] head = new byte[TarBlock];
            int length = gzip.ReadAtLeast(head, TarBlock, throwOnEndOfStream: false);
            return !OpensAsAnArchive(head.AsSpan(0, length));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
            or ArgumentException or NotSupportedException)
        {
            return false;
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
        long sum = 8 * ' ';
        for (int i = 0; i < TarBlock; i++)
        {
            if (i < 148 || i >= 156)
                sum += block[i];
        }
        return sum == stored;
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
                return (name[..^(extension.Length + 1)] + added).TrimEnd();
            }
        }
        int dot = name.LastIndexOf('.');
        return (dot > 0 ? name[..dot] : name + "~").TrimEnd();
    }

    private static readonly (string Extension, string Added)[] Extensions =
        [("gz", ""), ("gzip", ""), ("tgz", ".tar"), ("tpz", ".tar"), ("apk", ".tar")];

    /// <summary>
    /// Decompresses every member of <paramref name="path"/> into <paramref name="destination"/>
    /// (or nowhere, to test or measure it) and checks each member's CRC-32 and size against its
    /// trailer. Throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/>
    /// when a file cannot be read or written, <see cref="OperationCanceledException"/> on cancel.
    /// </summary>
    public static ReadResult Read(string path, Stream? destination, CancellationToken cancellationToken)
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

            long? trailer = FindTrailer(file, dataStart, dataStart + counted.BytesRead, crc.Finish(), (uint)memberLength);
            if (trailer is not { } at)
                return new ReadResult(total, Damaged: true, modified);
            position = at + 8;
        }
        while (position < file.Length);
        return new ReadResult(total, Damaged: false, modified);
    }

    // The fixed header and its optional fields (RFC 1952, 2.3); null when it is not a gzip
    // member header or ends early. Returns the member's MTIME.
    private static uint? ReadHeader(FileStream file)
    {
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
        if ((flags & FlagHeaderCrc) != 0)
            file.Position += 2;
        return file.Position <= file.Length ? BinaryPrimitives.ReadUInt32LittleEndian(fixedPart[4..8]) : null;
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

    // Where the member's 8-byte trailer (CRC-32, size mod 2^32) starts: in the window before where
    // DeflateStream stopped reading, the first place the trailer's bytes are followed by the end of
    // the file or by the next member. An empty member's trailer is eight zeros, which its own
    // compressed bytes may hold too, so what follows decides.
    private static long? FindTrailer(FileStream file, long dataStart, long readEnd, uint crc, uint size)
    {
        Span<byte> trailer = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(trailer, crc);
        BinaryPrimitives.WriteUInt32LittleEndian(trailer[4..], size);

        long from = Math.Max(dataStart, readEnd - TrailerWindow);
        long to = Math.Min(file.Length, readEnd + trailer.Length + NextHeader.Length);
        if (to - from < trailer.Length)
            return null;
        byte[] window = new byte[to - from];
        file.Position = from;
        file.ReadExactly(window);
        for (int start = 0; start + trailer.Length <= window.Length; start++)
        {
            int index = window.AsSpan(start).IndexOf(trailer);
            if (index < 0)
                return null;
            int after = start + index + trailer.Length;
            if (from + after == file.Length || window.AsSpan(after).StartsWith(NextHeader))
                return from + start + index;
            start += index;
        }
        return null;
    }

    private static ReadOnlySpan<byte> NextHeader => [0x1F, 0x8B, 0x08];

    // Counts what DeflateStream took from the file, so the trailer is looked for where it stopped.
    private sealed class CountingStream(Stream inner) : Stream
    {
        public long BytesRead { get; private set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = inner.Read(buffer, offset, count);
            BytesRead += read;
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            int read = inner.Read(buffer);
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
