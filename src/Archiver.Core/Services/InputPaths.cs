using Archiver.Core.Models;

namespace Archiver.Core.Services;

/// <summary>
/// T-F406: the paths a public operation is given are checked at its entry. An empty path or one
/// Windows cannot parse threw <see cref="ArgumentException"/> deep inside an engine, past catch
/// blocks that rightly take only I/O failures; here it becomes the "does not exist" error the
/// missing path already gives. <c>ArgumentException</c> is caught only around
/// <see cref="Path.GetFullPath(string)"/>, where the path is its one possible cause.
/// </summary>
internal static class InputPaths
{
    /// <summary>Why <paramref name="path"/> cannot be opened at all, or null when it can be tried.</summary>
    internal static CoreText? Problem(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return CoreMessages.Text(MessageCode.SourceNotFound, path ?? "");
        try
        {
            _ = Path.GetFullPath(path);
            return null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return CoreMessages.Text(MessageCode.SourceNotFound, path);
        }
    }
}
