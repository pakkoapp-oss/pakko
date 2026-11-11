using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Compression;

namespace Archiver.Core.Services.Zip;

/// <summary>
/// T-F318: the deflate compressors of one parallel archive run, reused across its small entries.
/// A new <see cref="DeflateStream"/> per entry builds a new zlib-ng state (about 330 KB) each time,
/// and under parallel load that costs a page-fault storm (dotnet/runtime#134700); a
/// <see cref="DeflateEncoder"/> reset between entries keeps its state. Concurrency is bounded by the
/// run's compute gate, so the pool never holds more deflaters than the run's window.
/// </summary>
internal sealed class DeflateEncoderPool : IDisposable
{
    private readonly ConcurrentBag<Deflater> _idle = [];
    private readonly int _quality;
    private int _created;

    public DeflateEncoderPool(CompressionLevel level) => _quality = QualityOf(level);

    /// <summary>How many deflaters this pool has built.</summary>
    internal int Created => Volatile.Read(ref _created);

    public Deflater Rent()
    {
        if (_idle.TryTake(out Deflater? idle))
            return idle;
        Interlocked.Increment(ref _created);
        return new Deflater(_quality);
    }

    /// <summary>Gives a deflater back after a compression that finished; one whose compression
    /// threw is disposed by the caller instead.</summary>
    public void Return(Deflater deflater) => _idle.Add(deflater);

    public void Dispose()
    {
        while (_idle.TryTake(out Deflater? deflater))
            deflater.Dispose();
    }

    // The zlib levels DeflateStream uses for each CompressionLevel (measured on .NET 11: the
    // encoder's stream at these qualities is within a byte of DeflateStream's, T-F318).
    internal static int QualityOf(CompressionLevel level) => level switch
    {
        CompressionLevel.NoCompression => 0,
        CompressionLevel.Fastest => 1,
        CompressionLevel.Optimal => 6,
        CompressionLevel.SmallestSize => 9,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, null),
    };

    /// <summary>One deflate compressor and the output buffer it keeps between entries.</summary>
    internal sealed class Deflater : IDisposable
    {
        // T-F499: .NET 11's DeflateEncoder.Reset() keeps the previous input in the window, and the
        // match finder still reads it: an input's stream came out a byte longer after some earlier
        // inputs (valid, but archives were no longer byte-identical across runs, T-F31). Compressing
        // zeros over what the previous input filled (its length and 2 KiB for the match finder's
        // lookahead, at most the whole window: 2 x 32 KiB and 1 KiB beyond) leaves the window as a
        // fresh encoder's; measured against thousands of earlier inputs and chains of them at
        // qualities 1, 6 and 9 (docs/DECISIONS.md, "T-F499").
        private const int ScrubSlack = 2 * 1024;
        private static readonly byte[] Scrub = new byte[(64 + 1) * 1024];

        private readonly DeflateEncoder _encoder;
        private byte[] _output = [];
        private int _filled = -1; // bytes the previous input filled; -1 before the first

        public Deflater(int quality) => _encoder = new DeflateEncoder(quality);

        /// <summary>
        /// Compresses <paramref name="input"/> as one complete deflate stream, the same bytes a
        /// fresh encoder writes. The result is valid until the next call.
        /// </summary>
        public ReadOnlySpan<byte> Compress(ReadOnlySpan<byte> input)
        {
            if (_filled >= 0)
                ScrubWindow(Math.Min(Scrub.Length, _filled + ScrubSlack));
            _filled = input.Length;
            _encoder.Reset();
            EnsureOutput(DeflateEncoder.GetMaxCompressedLength(input.Length));

            OperationStatus status = _encoder.Compress(input, _output, out int consumed, out int written, isFinalBlock: true);
            if (status != OperationStatus.Done || consumed != input.Length)
                throw new InvalidOperationException(
                    $"DeflateEncoder returned {status} after {consumed} of {input.Length} bytes.");
            return _output.AsSpan(0, written);
        }

        private void ScrubWindow(int length)
        {
            _encoder.Reset();
            EnsureOutput(DeflateEncoder.GetMaxCompressedLength(length));
            OperationStatus status = _encoder.Compress(Scrub.AsSpan(0, length), _output, out int consumed, out _, isFinalBlock: true);
            if (status != OperationStatus.Done || consumed != length)
                throw new InvalidOperationException($"DeflateEncoder returned {status} after {consumed} of {length} zero bytes.");
        }

        private void EnsureOutput(long bound)
        {
            if (_output.Length < bound)
                _output = new byte[bound];
        }

        public void Dispose() => _encoder.Dispose();
    }
}
