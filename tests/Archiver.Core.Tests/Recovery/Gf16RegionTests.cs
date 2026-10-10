using System.Buffers.Binary;
using System.Runtime.Intrinsics;
using Archiver.Core.Recovery;
using FluentAssertions;

namespace Archiver.Core.Tests.Recovery;

// T-F275: the region multiply-add every recovery block is built from. Both kernels are called
// directly, so the vector one is covered whatever CPU runs the tests, and each is compared with
// a per-word definition over Gf16.Mul.
public sealed class Gf16RegionTests
{
    public static TheoryData<int> Factors() => [0, 1, 2, 3, 0x100, 0x8000, 0xFFFF, 4107, 32856, 54321];

    [Theory]
    [MemberData(nameof(Factors))]
    public void MulAddScalar_EveryLengthModuloThirtyTwo_MatchesPerWordDefinition(int factor)
    {
        for (int length = 0; length <= 96; length += 2)
            AssertKernel((f, s, d) => Gf16Region.MulAddScalar(f, s, d), (ushort)factor, length);
    }

    [Theory]
    [MemberData(nameof(Factors))]
    public void MulAddVector_ProcessesWholeBlocks_MatchesPerWordDefinition(int factor)
    {
        for (int length = 0; length <= 96; length += 2)
        {
            byte[] src = RandomBytes(length, factor * 1000 + length);
            byte[] dst = RandomBytes(length, factor * 1000 + length + 1);
            byte[] expected = Expected((ushort)factor, src, dst);

            int done = Gf16Region.MulAddVector((ushort)factor, src, dst);

            done.Should().Be(length / 32 * 32);
            dst.AsSpan(0, done).ToArray().Should().Equal(expected.AsSpan(0, done).ToArray());
            dst.AsSpan(done).ToArray().Should().Equal(RandomBytes(length, factor * 1000 + length + 1).AsSpan(done).ToArray(),
                "the vector kernel leaves the tail to the scalar one");
        }
    }

    [Theory]
    [MemberData(nameof(Factors))]
    public void MulAdd_LongRegion_MatchesPerWordDefinition(int factor)
    {
        AssertKernel(Gf16Region.MulAdd, (ushort)factor, 65536 + 34);
    }

    // T-F375: x64 takes the SSSE3 lookup, so no x64 run reaches the portable one through MulAdd.
    [Fact]
    public void LookupPortable_NibbleIndexes_PicksTheTableBytes()
    {
        byte[] table = [.. Enumerable.Range(0, 16).Select(i => (byte)(i * 17 + 3))];
        byte[] nibbles = [15, 0, 7, 8, 1, 14, 2, 13, 3, 12, 4, 11, 5, 10, 6, 9];

        Vector128<byte> picked = Gf16Region.LookupPortable(Vector128.Create(table), Vector128.Create(nibbles));

        byte[] bytes = new byte[16];
        picked.CopyTo(bytes);
        bytes.Should().Equal(nibbles.Select(n => table[n]));
    }

    [Fact]
    public void MulAdd_LengthsDiffer_Throws()
    {
        Action act = () => Gf16Region.MulAdd(3, new byte[4], new byte[6]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void MulAdd_OddLength_Throws()
    {
        Action act = () => Gf16Region.MulAdd(3, new byte[5], new byte[5]);

        act.Should().Throw<ArgumentException>();
    }

    private delegate void Kernel(ushort factor, ReadOnlySpan<byte> src, Span<byte> dst);

    private static void AssertKernel(Kernel kernel, ushort factor, int length)
    {
        byte[] src = RandomBytes(length, factor + length);
        byte[] dst = RandomBytes(length, factor + length + 7);
        byte[] expected = Expected(factor, src, dst);

        kernel(factor, src, dst);

        dst.Should().Equal(expected, $"factor {factor}, length {length}");
    }

    private static byte[] Expected(ushort factor, byte[] src, byte[] dst)
    {
        byte[] expected = (byte[])dst.Clone();
        for (int i = 0; i < src.Length; i += 2)
        {
            ushort word = BinaryPrimitives.ReadUInt16LittleEndian(src.AsSpan(i));
            ushort acc = BinaryPrimitives.ReadUInt16LittleEndian(expected.AsSpan(i));
            BinaryPrimitives.WriteUInt16LittleEndian(expected.AsSpan(i), (ushort)(acc ^ Gf16.Mul(factor, word)));
        }
        return expected;
    }

    private static byte[] RandomBytes(int length, int seed)
    {
        byte[] bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }
}
