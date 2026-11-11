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
        private readonly DeflateEncoder _encoder;
        private byte[] _output = [];

        public Deflater(int quality) => _encoder = new DeflateEncoder(quality);

        /// <summary>
        /// Compresses <paramref name="input"/> as one complete deflate stream. The result is valid
        /// until the next call.
        /// </summary>
        public ReadOnlySpan<byte> Compress(ReadOnlySpan<byte> input)
        {
            _encoder.Reset();
            long bound = DeflateEncoder.GetMaxCompressedLength(input.Length);
            if (_output.Length < bound)
                _output = new byte[bound];

            OperationStatus status = _encoder.Compress(input, _output, out int consumed, out int written, isFinalBlock: true);
            if (status != OperationStatus.Done || consumed != input.Length)
                throw new InvalidOperationException(
                    $"DeflateEncoder returned {status} after {consumed} of {input.Length} bytes.");
            return _output.AsSpan(0, written);
        }

        public void Dispose() => _encoder.Dispose();
    }
}
