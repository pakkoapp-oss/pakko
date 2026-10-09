namespace Archiver.Core.Tests.Fuzz;

/// <summary>
/// Seeded byte-level mutator for the dependency-free fuzz tests (T-F240). Half of all offsets land
/// just after a ZIP record signature or a PAR2 packet magic, where the size, offset and count fields a parser trusts live;
/// random offsets alone almost never reach them in a small archive.
/// </summary>
internal static class ByteMutator
{
    private static readonly ulong[] InterestingValues =
        [0, 1, 0x7F, 0x80, 0xFF, 0x7FFF, 0x8000, 0xFFFF, 0x7FFF_FFFF, 0x8000_0000, 0xFFFF_FFFF, long.MaxValue, ulong.MaxValue];

    private static readonly byte[][] ZipSignatures =
    [
        [0x50, 0x4B, 0x03, 0x04], // local file header
        [0x50, 0x4B, 0x01, 0x02], // central directory header
        [0x50, 0x4B, 0x05, 0x06], // end of central directory
        [0x50, 0x4B, 0x06, 0x06], // Zip64 end of central directory
        [0x50, 0x4B, 0x06, 0x07], // Zip64 locator
        [0x50, 0x4B, 0x07, 0x08], // data descriptor
        [0x50, 0x41, 0x52, 0x32], // PAR2 packet header (T-F275)
    ];

    public static byte[] Mutate(byte[] input, Random rng, List<string> log)
    {
        List<int> anchors = FindSignatureOffsets(input);
        byte[] data = input;
        int count = rng.Next(1, 5);
        for (int i = 0; i < count && data.Length > 0; i++)
            data = ApplyOne(data, anchors, rng, log);
        return data;
    }

    private static byte[] ApplyOne(byte[] data, List<int> anchors, Random rng, List<string> log)
    {
        int offset = PickOffset(data.Length, anchors, rng);
        switch (rng.Next(6))
        {
            case 0:
            {
                byte[] copy = (byte[])data.Clone();
                int bit = rng.Next(8);
                copy[offset] ^= (byte)(1 << bit);
                log.Add($"flip {offset}:{bit}");
                return copy;
            }
            case 1:
            {
                byte[] copy = (byte[])data.Clone();
                copy[offset] = (byte)rng.Next(256);
                log.Add($"set {offset}={copy[offset]}");
                return copy;
            }
            case 2:
            {
                byte[] copy = (byte[])data.Clone();
                int width = 2 << rng.Next(3);
                ulong value = InterestingValues[rng.Next(InterestingValues.Length)];
                for (int b = 0; b < width && offset + b < copy.Length; b++)
                    copy[offset + b] = (byte)(value >> (8 * b));
                log.Add($"int{width * 8} {offset}={value}");
                return copy;
            }
            case 3:
                log.Add($"truncate {offset}");
                return data[..offset];
            case 4:
            {
                int length = Math.Min(rng.Next(1, 64), data.Length - offset);
                log.Add($"delete {offset}+{length}");
                return [.. data[..offset], .. data[(offset + length)..]];
            }
            default:
            {
                int length = Math.Min(rng.Next(1, 64), data.Length - offset);
                log.Add($"duplicate {offset}+{length}");
                return [.. data[..(offset + length)], .. data[offset..]];
            }
        }
    }

    private static int PickOffset(int length, List<int> anchors, Random rng)
    {
        if (anchors.Count == 0 || rng.Next(2) == 0)
            return rng.Next(length);
        int anchored = anchors[rng.Next(anchors.Count)] + rng.Next(64);
        return Math.Min(anchored, length - 1);
    }

    private static List<int> FindSignatureOffsets(byte[] data)
    {
        var offsets = new List<int>();
        for (int i = 0; i + 4 <= data.Length; i++)
        {
            foreach (byte[] signature in ZipSignatures)
            {
                if (data.AsSpan(i, 4).SequenceEqual(signature))
                    offsets.Add(i);
            }
        }
        return offsets;
    }
}
