using Archiver.Core.Recovery;
using FluentAssertions;

namespace Archiver.Core.Tests.Recovery;

// T-F275: the combination both create and repair run, against a per-word definition — with ranges
// and input batches forced small, so the range and batch seams are crossed on small data.
public sealed class Par2SliceCombinerTests
{
    [Theory]
    [InlineData(4, 1, 1, 1 << 24)]
    [InlineData(40, 7, 3, 1 << 24)]
    [InlineData(40, 7, 3, 12)]
    [InlineData(100, 130, 2, 8)]
    [InlineData(68, 3, 5, 4)]
    public void Combine_MatchesTheDefinition(int sliceSize, int inputCount, int outputCount, int maxRangeWidth)
    {
        var random = new Random(sliceSize * 31 + inputCount);
        byte[][] inputs = [.. Enumerable.Range(0, inputCount).Select(_ => RandomBytes(random, sliceSize))];
        ushort[,] coefficients = new ushort[outputCount, inputCount];
        for (int o = 0; o < outputCount; o++)
        {
            for (int i = 0; i < inputCount; i++)
                coefficients[o, i] = (ushort)random.Next(65536);
        }
        byte[][] outputs = [.. Enumerable.Range(0, outputCount).Select(_ => new byte[sliceSize])];
        long consumed = 0;

        Par2SliceCombiner.Combine(sliceSize, inputCount, outputCount, (o, i) => coefficients[o, i],
            (i, at, buffer) => inputs[i].AsSpan((int)at, buffer.Length).CopyTo(buffer),
            (o, at, data) => data.CopyTo(outputs[o].AsSpan((int)at)),
            bytes => consumed += bytes, CancellationToken.None, maxRangeWidth);

        for (int o = 0; o < outputCount; o++)
        {
            byte[] expected = new byte[sliceSize];
            for (int i = 0; i < inputCount; i++)
                Gf16Region.MulAddScalar(coefficients[o, i], inputs[i], expected);
            outputs[o].Should().Equal(expected, $"output {o}");
        }
        consumed.Should().Be((long)inputCount * sliceSize);
    }

    [Theory]
    [InlineData(4L, 1, 4)]
    [InlineData(1L << 30, 1, 16 << 20)]
    [InlineData(1L << 30, 32768, 8192)]
    [InlineData(262144L, 100, 262144)]
    [InlineData(6L, 1, 4)]
    public void RangeWidth_FitsTheBudgetAndStaysAMultipleOfFour(long sliceSize, int outputCount, int expected)
    {
        Par2SliceCombiner.RangeWidth(sliceSize, outputCount).Should().Be(expected);
    }

    private static byte[] RandomBytes(Random random, int length)
    {
        byte[] bytes = new byte[length];
        random.NextBytes(bytes);
        return bytes;
    }
}
