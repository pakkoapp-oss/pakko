namespace Archiver.Core.Tests.PathMatrix;

/// <summary>One shape a name can take, and why the matrix tries it.</summary>
/// <param name="Id">A stable id, used in failure messages and in <c>KnownFindings</c>.</param>
/// <param name="Name">The name itself.</param>
/// <param name="Why">What rule or bug this shape stands for.</param>
/// <param name="OnDisk">NTFS can hold the name (through <c>\\?\</c> where Win32 would change it).</param>
/// <param name="Quick">In the subset the quick tier runs disk operations over (T-F512 budget).</param>
internal sealed record NameShape(string Id, string Name, string Why, bool OnDisk = true, bool Quick = false)
{
    public override string ToString() => Id;
}

/// <summary>
/// T-F512: the names the path matrix tries. Each one stands for a rule an owner keeps or a bug met
/// before; a new shape goes here, never into one test. Non-ASCII names are built from code points,
/// so the file stays ASCII (text-encoding rule).
/// </summary>
internal static class NameShapes
{
    private static string U(params int[] codePoints) => string.Concat(codePoints.Select(char.ConvertFromUtf32));

    public static IReadOnlyList<NameShape> All { get; } =
    [
        new("plain", "ok.txt", "the control: every operation takes it", Quick: true),
        new("space-end", "a.txt ", "Windows drops a trailing space (T-F509)", Quick: true),
        new("dot-end", "a.txt.", "Windows drops a trailing dot, also in the middle of a path (T-F509)", Quick: true),
        new("dot-space-end", "a.txt. ", "both, in the order Windows drops them"),
        new("space-dot-end", "a.txt .", "both, the other order: the middle of a path keeps the space"),
        new("dots-end", "a.txt..", "two dots: the middle of a path drops only one"),
        new("only-dots", "...", "nothing left after the trim"),
        new("space-only-end", "a ", "a folder name that keeps its space in the middle of a path"),
        new("lead-space", " a.txt", "a leading space Windows keeps"),
        new("dot-step", ".", "a step, not a name", OnDisk: false),
        new("up-step", "..", "a step up, not a name", OnDisk: false),
        new("con", "CON", "a device on Windows 10 and Server 2022 (T-F475)", Quick: true),
        new("nul", "NUL", "a device that reads nothing"),
        new("com1", "COM1", "a numbered device"),
        new("lpt1", "LPT1", "a printer device"),
        new("con-ext", "CON.txt", "a device with an extension, still one on Windows 10"),
        new("con-space-ext", "CON .txt", "a device name before a space"),
        new("nul-zip", "nul.zip", "a device as an archive's name"),
        new("com-superscript", "COM" + U(0xB9), "COM with a superscript one, a device too"),
        new("alias", "LONGFI~1.TXT", "shaped as an 8.3 alias of a longer name (T-F462)", Quick: true),
        new("ads", "a.txt:s", "an alternate data stream", OnDisk: false, Quick: true),
        new("ads-data", "a.txt::$DATA", "the default data stream named", OnDisk: false),
        new("question", "a?b", "a wildcard Windows refuses in a name", OnDisk: false, Quick: true),
        new("less", "a<b", "a character Windows refuses", OnDisk: false),
        new("pipe", "a|b", "a character Windows refuses", OnDisk: false),
        new("star", "a*", "a wildcard Windows refuses", OnDisk: false),
        new("quote", "a\"b", "a character Windows refuses", OnDisk: false),
        new("tab", "a\tb", "a control character", OnDisk: false),
        new("cyrillic", U(0x444, 0x430, 0x439, 0x43B) + ".txt", "a name outside the code page (T-F376)"),
        new("nfc", U(0xE9) + ".txt", "e with acute, composed"),
        new("nfd", "e" + U(0x301) + ".txt", "e with acute, decomposed: another name than the composed one", Quick: true),
        new("rtl", "a" + U(0x202E) + "txt.exe", "a right-to-left override that hides the real extension"),
        new("zero-width", "a" + U(0x200B) + ".txt", "a zero-width space"),
        new("emoji", U(0x1F600) + ".txt", "a surrogate pair"),
        new("lone-surrogate", "a" + (char)0xD800 + ".txt", "half a surrogate pair, valid on NTFS, not UTF-16 text"),
        new("long-255", new string('n', 251) + ".txt", "the longest name NTFS takes", Quick: true),
        new("long-256", new string('n', 252) + ".txt", "one longer than NTFS takes", OnDisk: false),
        new("switch", "-sdel", "read as a pakko switch"),
        new("list", "@list", "read as a pakko list file"),
        new("percent", "%TEMP%", "an environment variable reference"),
        new("dollar", "a$b", "a dollar sign"),
        new("semicolon", "a;b", "a path-list separator"),
        new("tilde", "~a", "a leading tilde"),
        new("upper", "A.TXT", "the case of another name"),
        new("leading-dot", ".gitignore", "a leading dot, no extension (T-F264)"),
    ];

    public static IReadOnlyList<NameShape> Quick { get; } = [.. All.Where(s => s.Quick)];
}
