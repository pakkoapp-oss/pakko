using System.Runtime.InteropServices;
using System.Text;

namespace Archiver.CLI;

/// <summary>
/// T-F238: 7-Zip's <c>-scc{UTF-8|WIN|DOS}</c> console charset for stdout and stderr. Without it,
/// output uses the console code page, so a name the page cannot hold is written as '?' — 7-Zip
/// is lossy the same way by default. The values are 7-Zip's own sentinels (CP_ACP, CP_OEMCP,
/// CP_UTF8), resolved to real code pages only at run time so parsing stays machine-independent.
/// </summary>
public static partial class CliConsoleCharset
{
    /// <summary><c>-sccWIN</c>: the system ANSI code page (CP_ACP).</summary>
    public const int Ansi = 0;

    /// <summary><c>-sccDOS</c>: the system OEM code page (CP_OEMCP).</summary>
    public const int Oem = 1;

    /// <summary><c>-sccUTF-8</c>.</summary>
    public const int Utf8 = 65001;

    /// <summary>Maps a <c>-scc</c> value (case-insensitive) to its sentinel, or null if unsupported.</summary>
    public static int? TryParse(string name) => name.ToUpperInvariant() switch
    {
        "UTF-8" => Utf8,
        "WIN" => Ansi,
        "DOS" => Oem,
        _ => null,
    };

    /// <summary>The encoding for a sentinel. Never has a preamble: stdout is not seekable, so a
    /// <see cref="StreamWriter"/> would otherwise write a BOM into <c>&gt; list.txt</c>.</summary>
    public static Encoding CreateEncoding(int charset)
    {
        int codePage = charset switch
        {
            Ansi => (int)GetACP(),
            Oem => (int)GetOEMCP(),
            _ => charset,
        };
        return codePage == Utf8
            ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            : CodePagesEncodingProvider.Instance.GetEncoding(codePage) ?? Encoding.GetEncoding(codePage);
    }

    /// <summary>Replaces Console.Out and Console.Error with writers in <paramref name="charset"/>.
    /// Console.OutputEncoding is deliberately not set: on a real console that calls
    /// SetConsoleOutputCP, which would leave the user's shell switched after pakko exits.</summary>
    public static void Apply(int charset)
    {
        Encoding encoding = CreateEncoding(charset);
        Console.SetOut(CreateWriter(Console.OpenStandardOutput(), encoding));
        Console.SetError(CreateWriter(Console.OpenStandardError(), encoding));
    }

    // Synchronized: the conflict and password callbacks write to stderr off the main thread.
    // AutoFlush: nothing flushes a replacement writer at process exit.
    private static TextWriter CreateWriter(Stream stream, Encoding encoding) =>
        TextWriter.Synchronized(new StreamWriter(stream, encoding) { AutoFlush = true });

    [LibraryImport("kernel32.dll")]
    private static partial uint GetACP();

    [LibraryImport("kernel32.dll")]
    private static partial uint GetOEMCP();
}
