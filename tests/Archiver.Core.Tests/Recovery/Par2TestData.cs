using System.Buffers.Binary;
using Archiver.Core.Tests.Helpers;

namespace Archiver.Core.Tests.Recovery;

/// <summary>
/// Shared inputs of the PAR2 tests (T-F275). The golden sets under Fixtures/par2 were written by
/// par2cmdline 1.4.0 over <see cref="Content"/> (see Fixtures/par2/README.md); the splitter here
/// is deliberately simpler than Pakko's reader, so the comparison does not rely on the code under test.
/// </summary>
internal static class Par2TestData
{
    public static string GoldenDir => Path.Combine(FixtureHelper.FilesDir, "..", "par2");

    /// <summary>The bytes the golden sets protect: byte i = (i·31) XOR (i >> 7).</summary>
    public static byte[] Content(int length)
    {
        var bytes = new byte[length];
        for (int i = 0; i < length; i++)
            bytes[i] = (byte)((i * 31) ^ (i >> 7));
        return bytes;
    }

    public static string WriteContent(string folder, string name, int length)
    {
        string path = Path.Combine(folder, name);
        File.WriteAllBytes(path, Content(length));
        return path;
    }

    /// <summary>Every packet in the files, split on the header's own length.</summary>
    public static List<byte[]> Packets(params string[] paths)
    {
        var packets = new List<byte[]>();
        foreach (string path in paths)
        {
            byte[] file = File.ReadAllBytes(path);
            for (int offset = 0; offset < file.Length;)
            {
                long length = BinaryPrimitives.ReadInt64LittleEndian(file.AsSpan(offset + 8));
                packets.Add(file.AsSpan(offset, (int)length).ToArray());
                offset += (int)length;
            }
        }
        return packets;
    }

    public static string TypeOf(byte[] packet) => System.Text.Encoding.ASCII.GetString(packet, 48, 16).TrimEnd('\0');

    /// <summary>The distinct packets other than Creator, which names the program that wrote them.</summary>
    public static HashSet<string> NonCreatorPackets(params string[] paths) =>
        Packets(paths).Where(p => TypeOf(p) != "PAR 2.0\0Creator").Select(Convert.ToHexString).ToHashSet();
}
