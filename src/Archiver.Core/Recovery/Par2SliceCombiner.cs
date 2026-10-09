namespace Archiver.Core.Recovery;

/// <summary>How many slice-sized inputs a combination reads and outputs it writes.</summary>
internal readonly record struct Par2CombineShape(long SliceSize, int InputCount, int OutputCount);

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
    /// step consumed, InputCount · SliceSize in all. <paramref name="maxRangeWidth"/> lets a test
    /// force many ranges on small slices.</summary>
    internal static void Combine(Par2CombineShape shape, Func<int, int, ushort> coefficient,
        InputReader read, OutputWriter write, Action<long>? progress, CancellationToken cancellationToken, int maxRangeWidth = MaxRangeWidth)
    {
        int width = RangeWidth(Math.Min(shape.SliceSize, maxRangeWidth), shape.OutputCount);
        int batch = (int)Math.Clamp(InputBatchBudget / width, 1, MaxBatch);
        var buffers = new Buffers(NewBuffers(shape.OutputCount, width), NewBuffers(Math.Min(batch, shape.InputCount), width), new ushort[shape.OutputCount * batch]);
        var options = new ParallelOptions { CancellationToken = cancellationToken };

        for (long start = 0; start < shape.SliceSize; start += width)
        {
            int rangeWidth = (int)Math.Min(width, shape.SliceSize - start);
            foreach (byte[] output in buffers.Outputs)
                Array.Clear(output);

            for (int first = 0; first < shape.InputCount; first += batch)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int count = Math.Min(batch, shape.InputCount - first);
                for (int g = 0; g < count; g++)
                    read(first + g, start, buffers.Inputs[g].AsSpan(0, rangeWidth));
                FillFactors(buffers.Factors, batch, first, count, coefficient);
                ApplyBatch(buffers, batch, count, rangeWidth, options);
                progress?.Invoke((long)count * rangeWidth);
            }

            for (int o = 0; o < shape.OutputCount; o++)
                write(o, start, buffers.Outputs[o].AsSpan(0, rangeWidth));
        }
    }

    // factors[o * batch + g] = the coefficient of input first + g in output o.
    private static void FillFactors(ushort[] factors, int batch, int first, int count, Func<int, int, ushort> coefficient)
    {
        for (int o = 0; o < factors.Length / batch; o++)
        {
            for (int g = 0; g < count; g++)
                factors[o * batch + g] = coefficient(o, first + g);
        }
    }

    // Every output chunk gets the batch's inputs, chunks and outputs in parallel.
    private static void ApplyBatch(Buffers buffers, int batch, int count, int rangeWidth, ParallelOptions options)
    {
        int chunks = (rangeWidth + ChunkWidth - 1) / ChunkWidth;
        Parallel.For(0, buffers.Outputs.Length * chunks, options, task =>
        {
            int o = task / chunks;
            int from = task % chunks * ChunkWidth;
            int length = Math.Min(ChunkWidth, rangeWidth - from);
            Span<byte> destination = buffers.Outputs[o].AsSpan(from, length);
            for (int g = 0; g < count; g++)
                Gf16Region.MulAdd(buffers.Factors[o * batch + g], buffers.Inputs[g].AsSpan(from, length), destination);
        });
    }

    private sealed record Buffers(byte[][] Outputs, byte[][] Inputs, ushort[] Factors);

    /// <summary>The range of each slice one step processes: all outputs together fit the budget.
    /// A multiple of 4, as slice sizes are.</summary>
    internal static int RangeWidth(long sliceSize, int outputCount)
    {
        long width = Math.Min(Math.Min(sliceSize, MaxRangeWidth), OutputBudget / Math.Max(outputCount, 1));
        return (int)Math.Max(4, width & ~3L);
    }

    private static byte[][] NewBuffers(int count, int length)
    {
        byte[][] buffers = new byte[count][];
        for (int i = 0; i < count; i++)
            buffers[i] = new byte[length];
        return buffers;
    }
}
