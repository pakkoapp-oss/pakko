using System.Text;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// Builds raw USTAR-format tar archives byte-for-byte, without shelling out to tar.exe or any
/// third-party library. Used to construct the malicious archives (path traversal, symlink
/// escape) that TarProcessService.ExtractAsync must reject — these can't be created via normal
/// file operations since ".." and symlink-escape targets aren't real representable source paths.
/// </summary>
internal static class TarBuilder
{
    public sealed class Entry
    {
        public required string Name { get; init; }
        public byte[] Content { get; init; } = [];
        public char TypeFlag { get; init; } = '0';
        public string LinkName { get; init; } = string.Empty;

        // T-F204: the header's name field as raw bytes (a UTF-8 or OEM-code-page name, the way
        // GNU tar, 7-Zip or an old Windows tool writes it). Overrides Name when set.
        public byte[]? NameBytes { get; init; }
    }

    // A pax extended header ('x') carrying "path=<UTF-8>" for the entry that follows it.
    public static Entry PaxPath(string path)
    {
        byte[] body = Encoding.UTF8.GetBytes(" path=" + path + "\n");
        int length = body.Length;
        while (length.ToString(System.Globalization.CultureInfo.InvariantCulture).Length + body.Length != length)
            length = length.ToString(System.Globalization.CultureInfo.InvariantCulture).Length + body.Length;
        byte[] record = [.. Encoding.ASCII.GetBytes(length.ToString(System.Globalization.CultureInfo.InvariantCulture)), .. body];
        return new Entry { Name = "PaxHeader", TypeFlag = 'x', Content = record };
    }

    public static void WriteTar(string path, IEnumerable<Entry> entries)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        foreach (var entry in entries)
        {
            byte[] header = BuildHeader(entry.NameBytes ?? Encoding.ASCII.GetBytes(entry.Name), entry.Content.Length, entry.TypeFlag, entry.LinkName);
            fs.Write(header, 0, header.Length);
            if (entry.Content.Length > 0)
            {
                fs.Write(entry.Content, 0, entry.Content.Length);
                WritePadding(fs, entry.Content.Length);
            }
        }

        // Two 512-byte zero blocks terminate the archive.
        var end = new byte[1024];
        fs.Write(end, 0, end.Length);
    }

    private static void WritePadding(Stream stream, int contentLength)
    {
        int remainder = contentLength % 512;
        if (remainder == 0)
            return;
        var pad = new byte[512 - remainder];
        stream.Write(pad, 0, pad.Length);
    }

    private static byte[] BuildHeader(byte[] name, int size, char typeFlag, string linkName)
    {
        var header = new byte[512];

        Array.Copy(name, 0, header, 0, Math.Min(100, name.Length));
        SetField(header, 100, 8, "0000644\0");
        SetField(header, 108, 8, "0000000\0");
        SetField(header, 116, 8, "0000000\0");
        SetField(header, 124, 12, Convert.ToString(size, 8).PadLeft(11, '0') + "\0");
        SetField(header, 136, 12, "00000000000\0");
        SetField(header, 148, 8, "        "); // checksum placeholder (8 spaces)
        header[156] = (byte)typeFlag;
        SetField(header, 157, 100, linkName);
        SetField(header, 257, 6, "ustar\0");
        SetField(header, 263, 2, "00");
        SetField(header, 265, 32, "root");
        SetField(header, 297, 32, "root");
        SetField(header, 329, 8, "0000000\0");
        SetField(header, 337, 8, "0000000\0");

        int sum = 0;
        foreach (byte b in header)
            sum += b;
        SetField(header, 148, 8, Convert.ToString(sum, 8).PadLeft(6, '0') + "\0 ");

        return header;
    }

    private static void SetField(byte[] buffer, int offset, int length, string value)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(value);
        int n = Math.Min(length, bytes.Length);
        Array.Copy(bytes, 0, buffer, offset, n);
    }
}
