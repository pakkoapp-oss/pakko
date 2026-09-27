using System.Runtime.InteropServices;

namespace Archiver.Shell;

/// <summary>Paths read by <see cref="StdinPathList.Read"/>, or the reason the list was rejected.</summary>
internal sealed record StdinPathListResult(IReadOnlyList<string> Paths, string? Error);

/// <summary>
/// T-F235: Archiver.ShellExtension.dll hands an Explorer selection over on stdin instead of the
/// command line (32,767 characters, about 300 long paths). Format: UTF-16LE, every path followed by
/// one NUL, then one more NUL as the end marker — so a list cut short by a failed write is
/// rejected as a whole instead of running on part of the selection.
/// </summary>
internal static class StdinPathList
{
    public const string Flag = "--paths-stdin";

    // Matches ShellExtUtils.cpp's kMaxPathListBytes. Far past any selection Explorer can hand over;
    // it only bounds memory for a caller that never stops writing.
    public const int MaxBytes = 256 * 1024 * 1024;

    private const int ChunkBytes = 64 * 1024;

    /// <summary>Reads and validates the whole list. Never throws.</summary>
    public static StdinPathListResult Read(Stream input)
    {
        byte[] bytes;
        try
        {
            if (!TryReadBounded(input, out bytes))
                return Rejected("The path list is larger than the limit.");
        }
        catch (IOException ex)
        {
            return Rejected($"The path list could not be read: {ex.Message}");
        }

        if (bytes.Length % sizeof(char) != 0)
            return Rejected("The path list is not UTF-16 text.");

        // Built from the raw code units, not Encoding.Unicode: an NTFS name may hold an unpaired
        // surrogate, which a decoder would replace with U+FFFD and so name a different file.
        string text = new(MemoryMarshal.Cast<byte, char>(bytes));
        if (!text.EndsWith("\0\0", StringComparison.Ordinal))
            return Rejected("The path list ended early.");

        string[] paths = text[..^2].Split('\0');
        if (Array.Exists(paths, p => p.Length == 0))
            return Rejected("The path list has an empty entry.");

        return new StdinPathListResult(paths, null);
    }

    private static bool TryReadBounded(Stream input, out byte[] bytes)
    {
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[ChunkBytes];
        int read;
        while ((read = input.Read(chunk, 0, chunk.Length)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxBytes)
            {
                bytes = [];
                return false;
            }
        }

        bytes = buffer.ToArray();
        return true;
    }

    private static StdinPathListResult Rejected(string reason) => new([], reason);
}
