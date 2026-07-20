using System.Buffers.Binary;

namespace Archiver.Core.IO;

/// <summary>
/// Standalone CRC-32 (ISO 3309 / ZIP polynomial 0xEDB88320) — Archiver.Core takes no NuGet
/// dependencies, so <c>System.IO.Hashing.Crc32</c> is not an option, and
/// <see cref="System.IO.Compression.ZipArchiveEntry.Crc32"/> only exposes the value stored in
/// the entry's header; .NET does not verify it against the decompressed bytes on read.
/// Public (not internal) so Archiver.App's FileItem can reuse it for the pending-list CRC-32
/// column instead of adding a second implementation or a hashing NuGet package there.
/// </summary>
public static class Crc32
{
    // T-F128 follow-up: slice-by-8 (a standard CRC-32 acceleration technique — see e.g. zlib's
    // crc32.c) replaced a byte-at-a-time single-table lookup after a real performance test found
    // Pakko's own hashing ~9x slower than 7-Zip's own CRC-32 on a 300 MB file. Processes 8 bytes
    // per iteration using 8 precomputed tables instead of 1 byte using 1 table; produces the exact
    // same CRC-32 values as before (same polynomial/base table, just reorganized math) — verified
    // against every existing known-value/7za cross-check test. The 8 tables are one flat
    // 2048-entry array (not uint[8][256]) — a jagged array needs two pointer dereferences per
    // lookup (fetch the row array, then index into it); a flat array is one contiguous block, one
    // dereference, better cache locality. Confirmed via a throwaway in-memory-only benchmark
    // during T-F128 that isolated CRC-32 compute time from file I/O — I/O was never the
    // bottleneck (>1.8 GB/s even via async ReadAsync), the table-lookup math was.
    private static readonly uint[] Tables = BuildTables();

    private static uint[] BuildTables()
    {
        var t0 = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            t0[i] = c;
        }

        var tables = new uint[8 * 256];
        t0.CopyTo(tables, 0);
        for (int slice = 1; slice < 8; slice++)
        {
            int prevBase = (slice - 1) * 256;
            int curBase = slice * 256;
            for (int i = 0; i < 256; i++)
                tables[curBase + i] = (tables[prevBase + i] >> 8) ^ t0[tables[prevBase + i] & 0xFF];
        }
        return tables;
    }

    /// <summary>Computes the CRC-32 of all remaining bytes in <paramref name="stream"/>.</summary>
    public static uint Compute(Stream stream)
    {
        var acc = new Accumulator();
        Span<byte> buffer = stackalloc byte[8192];
        int read;
        while ((read = stream.Read(buffer)) > 0)
            acc.Update(buffer[..read]);
        return acc.Finish();
    }

    /// <summary>
    /// T-F128 follow-up: combines two independently-computed CRC-32 values — <paramref name="crc1"/>
    /// over some data A, <paramref name="crc2"/> over data B of length <paramref name="len2"/> — into
    /// the CRC-32 of A followed by B, without re-reading either block. This is what makes hashing one
    /// large file in parallel possible: split the file into chunks, hash each chunk independently
    /// (fresh <see cref="Accumulator"/> per chunk, no shared state, embarrassingly parallel), then
    /// fold the per-chunk results together in order with this method — an O(log len2) operation, not
    /// proportional to the data size.
    /// <para>
    /// Standard technique (not invented here) — this is a faithful reimplementation of zlib's public
    /// domain <c>crc32_combine</c> (see zlib's <c>crc32.c</c>), which treats the CRC register as an
    /// element of GF(2)[x]/(the CRC-32 polynomial) and represents "shift the CRC register as if N zero
    /// bytes were appended" as a 32x32 bit matrix, computed via repeated squaring so the whole
    /// operation costs O(log N) bit-matrix multiplications regardless of how large N (len2) is. Both
    /// <paramref name="crc1"/>/<paramref name="crc2"/> are the normal finished (post-<see cref="Accumulator.Finish"/>,
    /// i.e. XOR'd with 0xFFFFFFFF) CRC-32 values — same convention zlib's own public API uses — not
    /// the raw un-finished accumulator register.
    /// </para>
    /// </summary>
    public static uint Combine(uint crc1, uint crc2, long len2)
    {
        if (len2 <= 0) return crc1;

        Span<uint> even = stackalloc uint[32];
        Span<uint> odd = stackalloc uint[32];

        // Operator for shifting in one zero bit.
        odd[0] = 0xEDB88320;
        uint row = 1;
        for (int n = 1; n < 32; n++)
        {
            odd[n] = row;
            row <<= 1;
        }

        MatrixSquare(even, odd); // operator for two zero bits
        MatrixSquare(odd, even); // operator for four zero bits

        long len = len2;
        do
        {
            MatrixSquare(even, odd);
            if ((len & 1) != 0)
                crc1 = MatrixTimes(even, crc1);
            len >>= 1;
            if (len == 0) break;

            MatrixSquare(odd, even);
            if ((len & 1) != 0)
                crc1 = MatrixTimes(odd, crc1);
            len >>= 1;
        } while (len != 0);

        return crc1 ^ crc2;
    }

    private static uint MatrixTimes(ReadOnlySpan<uint> mat, uint vec)
    {
        uint sum = 0;
        int n = 0;
        while (vec != 0)
        {
            if ((vec & 1) != 0)
                sum ^= mat[n];
            vec >>= 1;
            n++;
        }
        return sum;
    }

    private static void MatrixSquare(Span<uint> square, ReadOnlySpan<uint> mat)
    {
        for (int n = 0; n < 32; n++)
            square[n] = MatrixTimes(mat, mat[n]);
    }

    /// <summary>
    /// Running CRC-32 state for computing a hash incrementally across chunks read from a
    /// stream that is simultaneously being piped elsewhere (e.g. into a compressor) — used by
    /// <c>Services.Zip.ZipEntryWriter</c> so uncompressed bytes are hashed in the same single
    /// read pass as compression, instead of requiring a second full read of the source file.
    /// </summary>
    public struct Accumulator
    {
        private uint _crc = 0xFFFFFFFF;

        public Accumulator() { }

        public void Update(ReadOnlySpan<byte> data)
        {
            uint crc = _crc;
            var t = Tables;
            int i = 0;
            int n = data.Length;

            while (n - i >= 8)
            {
                uint v1 = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(i, 4)) ^ crc;
                uint v2 = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(i + 4, 4));

                crc = t[7 * 256 + (int)(v1 & 0xFF)] ^ t[6 * 256 + (int)((v1 >> 8) & 0xFF)]
                    ^ t[5 * 256 + (int)((v1 >> 16) & 0xFF)] ^ t[4 * 256 + (int)(v1 >> 24)]
                    ^ t[3 * 256 + (int)(v2 & 0xFF)] ^ t[2 * 256 + (int)((v2 >> 8) & 0xFF)]
                    ^ t[1 * 256 + (int)((v2 >> 16) & 0xFF)] ^ t[(int)(v2 >> 24)];

                i += 8;
            }

            for (; i < n; i++)
                crc = t[(int)((crc ^ data[i]) & 0xFF)] ^ (crc >> 8);

            _crc = crc;
        }

        public readonly uint Finish() => _crc ^ 0xFFFFFFFF;
    }
}
