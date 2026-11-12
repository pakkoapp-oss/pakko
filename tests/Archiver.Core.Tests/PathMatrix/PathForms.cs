namespace Archiver.Core.Tests.PathMatrix;

/// <summary>Where in a path the shape stands.</summary>
internal enum ShapePosition
{
    /// <summary>The path's own name: <c>root\shape</c>.</summary>
    Last,

    /// <summary>A folder on the way: <c>root\shape\f.txt</c> - Windows trims less there (T-F509).</summary>
    Middle,
}

/// <summary>One way to spell a path that names the same place, and why the matrix tries it.</summary>
/// <param name="Id">A stable id, used in failure messages.</param>
/// <param name="Why">What rule or bug this form stands for.</param>
/// <param name="Literal">Win32 leaves the path as typed (<c>\\?\</c>): no trim, no normalization.</param>
/// <param name="Spell">The path to the shape's place, from the folder's full path and the parts below it.</param>
internal sealed record PathForm(string Id, string Why, bool Literal, Func<string, IReadOnlyList<string>, string> Spell)
{
    public override string ToString() => Id;
}

/// <summary>
/// T-F512: the spellings of a path the matrix tries. Every form names the same place as
/// <see cref="Absolute"/>; a form that needs the disk (an 8.3 short name, a junction on the way)
/// is built by the disk layer, not here.
/// </summary>
internal static class PathForms
{
    private static string Join(string folder, IReadOnlyList<string> parts) => folder + "\\" + string.Join('\\', parts);

    // "C:\pm\x" as "\\localhost\C$\pm\x".
    private static string AsUnc(string folder) => @"\\localhost\" + folder[0] + "$" + folder[2..];

    public static PathForm Absolute { get; } = new("absolute", "the plain full path", false, Join);

    public static IReadOnlyList<PathForm> All { get; } =
    [
        Absolute,
        new("long-prefix", @"\\?\: Win32 keeps the name as typed (CLI.md)", true, (f, p) => @"\\?\" + Join(f, p)),
        new("device-prefix", @"\\.\: normalized like a plain path, but a device namespace", false, (f, p) => @"\\.\" + Join(f, p)),
        new("unc", "the same folder through the administrative share", false, (f, p) => Join(AsUnc(f), p)),
        new("unc-long", @"\\?\UNC\: the share, literal", true, (f, p) => @"\\?\UNC\" + Join(AsUnc(f), p)[2..]),
        new("forward-slash", "'/' between the parts", false, (f, p) => Join(f, p).Replace('\\', '/')),
        new("doubled", "every separator doubled after the root", false, (f, p) => f[..3] + Join(f, p)[3..].Replace("\\", "\\\\", StringComparison.Ordinal)),
        new("trailing-separator", "a separator after the name, as a shell completion leaves it (T-F153)", false, (f, p) => Join(f, p) + "\\"),
        new("up-and-back", @"a step down and back up before the name: ..\", false, (f, p) => Join(f + @"\y\..", p)),
        new("dot-step", @"a .\ step before the name", false, (f, p) => Join(f + @"\.", p)),
        new("over-260", "a folder on the way makes the path longer than MAX_PATH", false, (f, p) => Join(f + "\\" + new string('p', 250), p)),
        new("relative", "relative to the current folder", false, (f, p) => Join(Path.GetRelativePath(Environment.CurrentDirectory, f), p)),
    ];

    /// <summary>The parts below the folder for a shape at a position.</summary>
    public static IReadOnlyList<string> Parts(NameShape shape, ShapePosition position) =>
        position == ShapePosition.Last ? [shape.Name] : [shape.Name, "f.txt"];
}
