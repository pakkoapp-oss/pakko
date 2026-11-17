using System.Buffers.Binary;

namespace Archiver.Core.Services.Sandbox;

/// <summary>
/// T-F550: the lines a "-t" run printed, each in tar.exe's own bytes as the run handed it over (its
/// '\r' too: the reader drops it, as for a listing read once), kept for T-F465's check so that sending
/// names back needs no second "-t". Held in blocks, never one array: each line is its length (4 bytes)
/// and then its bytes, and a line that does not fit the rest of a block starts a new one. What a block
/// leaves unused is less than the next line, so the store holds under twice (the listing's bytes plus
/// 4 a line), plus one block; the listing is bounded by the run's StdOutLimit.
/// </summary>
internal sealed class TarPrintedNames(int blockSize = TarPrintedNames.DefaultBlockSize)
{
    internal const int DefaultBlockSize = 1 << 20;

    private readonly List<byte[]> _blocks = [];
    private int _used;

    public void Add(ReadOnlySpan<byte> line)
    {
        if (line.IsEmpty)
            return;
        int needed = checked(sizeof(int) + line.Length);
        if (_blocks.Count == 0 || _blocks[^1].Length - _used < needed)
        {
            _blocks.Add(new byte[Math.Max(blockSize, needed)]);
            _used = 0;
        }
        Span<byte> free = _blocks[^1].AsSpan(_used);
        BinaryPrimitives.WriteInt32LittleEndian(free, line.Length);
        line.CopyTo(free[sizeof(int)..]);
        _used += needed;
    }

    /// <summary>Each line, in the order printed.</summary>
    public void ForEach(StdOutLineHandler onLine)
    {
        for (int b = 0; b < _blocks.Count; b++)
        {
            byte[] block = _blocks[b];
            int end = b == _blocks.Count - 1 ? _used : block.Length;
            int position = 0;
            // An empty line is never stored, so a zero length is a block's unused rest.
            while (end - position >= sizeof(int)
                && BinaryPrimitives.ReadInt32LittleEndian(block.AsSpan(position)) is > 0 and int length
                && length <= end - position - sizeof(int))
            {
                onLine(block.AsSpan(position + sizeof(int), length));
                position += sizeof(int) + length;
            }
        }
    }

    internal int BlockCount => _blocks.Count;
}
