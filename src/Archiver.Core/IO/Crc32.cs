namespace Archiver.Core.IO;

/// <summary>
/// Standalone CRC-32 (ISO 3309 / ZIP polynomial 0xEDB88320) — Archiver.Core takes no NuGet
/// dependencies, so <c>System.IO.Hashing.Crc32</c> is not an option, and
/// <see cref="System.IO.Compression.ZipArchiveEntry.Crc32"/> only exposes the value stored in
/// the entry's header; .NET does not verify it against the decompressed bytes on read.
/// Public (not internal) so Archiver.App's FileItem can reuse it for the pending-list CRC-32
/// column instead of adding a second implementation or a hashing NuGet package there.
/// </summary>
public static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            table[i] = c;
        }
        return table;
    }

    /// <summary>Computes the CRC-32 of all remaining bytes in <paramref name="stream"/>.</summary>
    public static uint Compute(Stream stream)
    {
        var acc = new Accumulator();
        Span<byte> buffer = stackalloc byte[8192];
        int read;
        while ((read = stream.Read(buffer)) > 0)
            acc.Update(buffer[..read]);
        return acc.Finish();
    }

    /// <summary>
    /// Running CRC-32 state for computing a hash incrementally across chunks read from a
    /// stream that is simultaneously being piped elsewhere (e.g. into a compressor) — used by
    /// <c>Services.Zip.ZipEntryWriter</c> so uncompressed bytes are hashed in the same single
    /// read pass as compression, instead of requiring a second full read of the source file.
    /// </summary>
    public struct Accumulator
    {
        private uint _crc = 0xFFFFFFFF;

        public Accumulator() { }

        public void Update(ReadOnlySpan<byte> data)
        {
            uint crc = _crc;
            foreach (byte b in data)
                crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            _crc = crc;
        }

        public readonly uint Finish() => _crc ^ 0xFFFFFFFF;
    }
}
