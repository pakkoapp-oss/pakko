namespace Archiver.Core.Recovery;

/// <summary>
/// output o = Σ_i coefficient(o, i) · input i, every input and output one slice long (T-F275).
/// Creating recovery data is this with the inputs the file's slices; repairing is this with the
/// inputs the intact slices plus the chosen recovery blocks. Memory stays bounded whatever the
/// slice size: the slice is processed in ranges, each range reading only its part of every input.
/// </summary>
internal static class Par2SliceCombiner
{
    private const long OutputBudget = 256L << 20;
    private const int MaxRangeWidth = 16 << 20;
    private const long InputBatchBudget = 64L << 20;
    private const int MaxBatch = 64;
    private const int ChunkWidth = 64 << 10;

    /// <summary>Fills <paramref name="buffer"/> with input <paramref name="input"/>'s bytes at
    /// <paramref name="offsetInSlice"/>, zero past its end.</summary>
    internal delegate void InputReader(int input, long offsetInSlice, Span<byte> buffer);

    internal delegate void OutputWriter(int output, long offsetInSlice, ReadOnlySpan<byte> data);

    /// <summary>Runs the whole combination; <paramref name="progress"/> gets the input bytes each
    /// step consumed, inputCount · sliceSize in all.</summary>
    internal static void Combine(long sliceSize, int inputCount, int outputCount, Func<int, int, ushort> coefficient,
        InputReader read, OutputWriter write, Action<long>? progress, CancellationToken cancellationToken)
    {
        int width = RangeWidth(sliceSize, outputCount);
        int batch = (int)Math.Clamp(InputBatchBudget / width, 1, MaxBatch);
        byte[][] outputs = NewBuffers(outputCount, width);
        byte[][] inputs = NewBuffers(Math.Min(batch, inputCount), width);
        var factors = new ushort[outputCount * batch];
        var options = new ParallelOptions { CancellationToken = cancellationToken };

        for (long start = 0; start < sliceSize; start += width)
        {
            int rangeWidth = (int)Math.Min(width, sliceSize - start);
            foreach (byte[] output in outputs)
                Array.Clear(output);

            for (int first = 0; first < inputCount; first += batch)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int count = Math.Min(batch, inputCount - first);
                for (int g = 0; g < count; g++)
                    read(first + g, start, inputs[g].AsSpan(0, rangeWidth));
                for (int o = 0; o < outputCount; o++)
                {
                    for (int g = 0; g < count; g++)
                        factors[o * batch + g] = coefficient(o, first + g);
                }

                int chunks = (rangeWidth + ChunkWidth - 1) / ChunkWidth;
                Parallel.For(0, outputCount * chunks, options, task =>
                {
                    int o = task / chunks;
                    int from = task % chunks * ChunkWidth;
                    int length = Math.Min(ChunkWidth, rangeWidth - from);
                    Span<byte> destination = outputs[o].AsSpan(from, length);
                    for (int g = 0; g < count; g++)
                        Gf16Region.MulAdd(factors[o * batch + g], inputs[g].AsSpan(from, length), destination);
                });
                progress?.Invoke((long)count * rangeWidth);
            }

            for (int o = 0; o < outputCount; o++)
                write(o, start, outputs[o].AsSpan(0, rangeWidth));
        }
    }

    /// <summary>The range of each slice one step processes: all outputs together fit the budget.
    /// A multiple of 4, as slice sizes are.</summary>
    internal static int RangeWidth(long sliceSize, int outputCount)
    {
        long width = Math.Min(Math.Min(sliceSize, MaxRangeWidth), OutputBudget / Math.Max(outputCount, 1));
        return (int)Math.Max(4, width & ~3L);
    }

    private static byte[][] NewBuffers(int count, int length)
    {
        var buffers = new byte[count][];
        for (int i = 0; i < count; i++)
            buffers[i] = new byte[length];
        return buffers;
    }
}
