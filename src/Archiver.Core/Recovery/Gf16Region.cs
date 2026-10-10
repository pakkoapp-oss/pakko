using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Archiver.Core.Recovery;

/// <summary>
/// dst ^= factor · src over a region of 16-bit little-endian words — the one operation both
/// creating and repairing recovery data spend their time in (T-F275).
/// </summary>
internal static class Gf16Region
{
    private const int VectorBlock = 32;

    internal static void MulAdd(ushort factor, ReadOnlySpan<byte> src, Span<byte> dst)
    {
        if (src.Length != dst.Length || (src.Length & 1) != 0)
            throw new ArgumentException("Regions must have the same even length.", nameof(src));
        if (factor == 0)
            return;
        int done = Vector128.IsHardwareAccelerated ? MulAddVector(factor, src, dst) : 0;
        MulAddScalar(factor, src[done..], dst[done..]);
    }

    /// <summary>The reference kernel: one table multiply per word.</summary>
    internal static void MulAddScalar(ushort factor, ReadOnlySpan<byte> src, Span<byte> dst)
    {
        for (int i = 0; i + 2 <= src.Length; i += 2)
        {
            ushort word = BinaryPrimitives.ReadUInt16LittleEndian(src[i..]);
            ushort acc = BinaryPrimitives.ReadUInt16LittleEndian(dst[i..]);
            BinaryPrimitives.WriteUInt16LittleEndian(dst[i..], (ushort)(acc ^ Gf16.Mul(factor, word)));
        }
    }

    /// <summary>
    /// The split-nibble method (as in ParPar's <c>gf16_shuffle</c>): factor · w is the XOR of
    /// factor · (nibble k of w, shifted into place) for k = 0..3, each a 16-entry table, looked up
    /// 16 bytes at a time with a byte shuffle (pshufb on x64, TBL on ARM64). Processes whole
    /// 32-byte blocks and returns how many bytes it did; the caller finishes the tail.
    /// </summary>
    internal static int MulAddVector(ushort factor, ReadOnlySpan<byte> src, Span<byte> dst)
    {
        Span<byte> lowTables = stackalloc byte[64];
        Span<byte> highTables = stackalloc byte[64];
        for (int k = 0; k < 4; k++)
        {
            for (int n = 0; n < 16; n++)
            {
                ushort product = Gf16.Mul(factor, (ushort)(n << (4 * k)));
                lowTables[k * 16 + n] = (byte)product;
                highTables[k * 16 + n] = (byte)(product >> 8);
            }
        }
        var lo0 = Vector128.Create((ReadOnlySpan<byte>)lowTables[..16]);
        var lo1 = Vector128.Create((ReadOnlySpan<byte>)lowTables[16..32]);
        var lo2 = Vector128.Create((ReadOnlySpan<byte>)lowTables[32..48]);
        var lo3 = Vector128.Create((ReadOnlySpan<byte>)lowTables[48..]);
        var hi0 = Vector128.Create((ReadOnlySpan<byte>)highTables[..16]);
        var hi1 = Vector128.Create((ReadOnlySpan<byte>)highTables[16..32]);
        var hi2 = Vector128.Create((ReadOnlySpan<byte>)highTables[32..48]);
        var hi3 = Vector128.Create((ReadOnlySpan<byte>)highTables[48..]);
        var nibble = Vector128.Create((byte)0x0F);
        var lowByte = Vector128.Create((ushort)0x00FF);

        int done = 0;
        for (; done + VectorBlock <= src.Length; done += VectorBlock)
        {
            Vector128<ushort> w0 = Vector128.Create(src.Slice(done, 16)).AsUInt16();
            Vector128<ushort> w1 = Vector128.Create(src.Slice(done + 16, 16)).AsUInt16();
            var low = Vector128.Narrow(w0 & lowByte, w1 & lowByte);
            var high = Vector128.Narrow(w0 >>> 8, w1 >>> 8);
            Vector128<byte> n0 = low & nibble;
            Vector128<byte> n1 = low >>> 4;
            Vector128<byte> n2 = high & nibble;
            Vector128<byte> n3 = high >>> 4;

            Vector128<byte> resultLow = Lookup(lo0, n0) ^ Lookup(lo1, n1)
                ^ Lookup(lo2, n2) ^ Lookup(lo3, n3);
            Vector128<byte> resultHigh = Lookup(hi0, n0) ^ Lookup(hi1, n1)
                ^ Lookup(hi2, n2) ^ Lookup(hi3, n3);

            Vector128<ushort> r0 = Vector128.WidenLower(resultLow) | (Vector128.WidenLower(resultHigh) << 8);
            Vector128<ushort> r1 = Vector128.WidenUpper(resultLow) | (Vector128.WidenUpper(resultHigh) << 8);

            Span<byte> d0 = dst.Slice(done, 16);
            Span<byte> d1 = dst.Slice(done + 16, 16);
            (Vector128.Create((ReadOnlySpan<byte>)d0) ^ r0.AsByte()).CopyTo(d0);
            (Vector128.Create((ReadOnlySpan<byte>)d1) ^ r1.AsByte()).CopyTo(d1);
        }
        return done;
    }

    // T-F375: the Native AOT build compiles for a baseline x64 processor without SSSE3, where
    // Vector128.ShuffleNative becomes a per-byte fallback (six times slower over a whole archive);
    // asking for SSSE3 by name is a run-time check there and pshufb when it is present.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<byte> Lookup(Vector128<byte> table, Vector128<byte> nibbles) =>
        Ssse3.IsSupported ? Ssse3.Shuffle(table, nibbles) : Vector128.ShuffleNative(table, nibbles);
}
