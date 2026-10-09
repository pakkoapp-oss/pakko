using Archiver.Core.Recovery;
using FluentAssertions;

namespace Archiver.Core.Tests.Recovery;

// T-F275: the field PAR 2.0's Reed-Solomon code works in. The expected values come from the
// specification, and the multiplication is checked against a bit-by-bit carry-less multiply that
// shares nothing with the log/exp tables under test.
public sealed class Gf16Tests
{
    [Fact]
    public void InputConstants_FirstEleven_MatchTheSpecification()
    {
        Gf16.InputConstants(11).Should().Equal(2, 4, 16, 128, 256, 2048, 8192, 16384, 4107, 32856, 17132);
    }

    [Fact]
    public void InputConstants_MaximumCount_AreDistinctAndNonZero()
    {
        ushort[] constants = Gf16.InputConstants(Par2Limits.MaxInputSlices);

        constants.Should().HaveCount(Par2Limits.MaxInputSlices).And.OnlyHaveUniqueItems().And.NotContain(0);
    }

    [Fact]
    public void Mul_AgreesWithCarryLessMultiply()
    {
        var random = new Random(275);
        for (int i = 0; i < 20000; i++)
        {
            ushort a = (ushort)random.Next(65536);
            ushort b = (ushort)random.Next(65536);
            Gf16.Mul(a, b).Should().Be(SlowMul(a, b), $"{a} * {b}");
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 0xFFFF)]
    [InlineData(1, 0xFFFF)]
    [InlineData(0xFFFF, 0xFFFF)]
    [InlineData(0x8000, 2)]
    public void Mul_Edges_AgreeWithCarryLessMultiply(int a, int b)
    {
        Gf16.Mul((ushort)a, (ushort)b).Should().Be(SlowMul((ushort)a, (ushort)b));
    }

    [Fact]
    public void Inv_EveryNonZeroElement_MultipliesToOne()
    {
        for (int a = 1; a <= 0xFFFF; a++)
            Gf16.Mul((ushort)a, Gf16.Inv((ushort)a)).Should().Be(1);
    }

    [Fact]
    public void Inv_Zero_Throws()
    {
        Action act = () => Gf16.Inv(0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(2u)]
    [InlineData(65534u)]
    [InlineData(65535u)]
    [InlineData(65536u)]
    [InlineData(uint.MaxValue)]
    public void Pow_AgreesWithRepeatedMultiplication(uint exponent)
    {
        foreach (ushort a in new ushort[] { 0, 1, 2, 3, 4107, 0xFFFF })
            Gf16.Pow(a, exponent).Should().Be(SlowPow(a, exponent), $"{a}^{exponent}");
    }

    [Fact]
    public void Pow_ZeroExponent_IsOneEvenForZero()
    {
        Gf16.Pow(0, 0).Should().Be(1);
        Gf16.Pow(12345, 0).Should().Be(1);
    }

    private static ushort SlowMul(ushort a, ushort b)
    {
        uint product = 0;
        uint shifted = a;
        for (int bit = 0; bit < 16; bit++)
        {
            if ((b & (1 << bit)) != 0)
                product ^= shifted;
            shifted <<= 1;
        }
        for (int bit = 31; bit >= 16; bit--)
        {
            if ((product & (1u << bit)) != 0)
                product ^= 0x1100Bu << (bit - 16);
        }
        return (ushort)product;
    }

    // Square-and-multiply over SlowMul; the multiplicative group has order 65535.
    private static ushort SlowPow(ushort a, uint exponent)
    {
        if (exponent == 0)
            return 1;
        if (a == 0)
            return 0;
        uint e = exponent % 65535;
        ushort result = 1;
        ushort square = a;
        while (e != 0)
        {
            if ((e & 1) != 0)
                result = SlowMul(result, square);
            square = SlowMul(square, square);
            e >>= 1;
        }
        return result;
    }
}
