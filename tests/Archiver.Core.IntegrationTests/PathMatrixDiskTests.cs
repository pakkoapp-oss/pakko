using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.Versioning;
using System.Text;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.PathMatrix;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// T-F512 (b): the real operations on disk over all pairs of name shape, path form, position, twin,
/// role and engine. Each case gets its own folder; the named place holds bytes marked LITERAL, a
/// twin Windows would open in its place holds bytes marked TWIN. Every case, whatever the outcome:
/// no throw, the twin untouched, no TWIN bytes in what was written, nothing outside the case's
/// folder. A clean result must hold the LITERAL bytes, and the plain name must give one. The quick
/// tier runs the quick shapes; <c>Category=Fuzz</c> samples the cases over every shape with the
/// T-F240 knobs.
/// </summary>
[SupportedOSPlatform("windows")]
[Collection("TarSandbox")]
public sealed class PathMatrixDiskTests : IDisposable
{
    private const string Literal = @"\\?\";
    private const string TwinMark = "TWIN-";
    private static readonly TimeSpan CaseTimeout = TimeSpan.FromSeconds(60);

    private enum Role { CreateSource, CreateDestination, ExtractArchive, ExtractDestination }

    private enum Engine { Zip, TarGz }

    // Only safe outcomes may be listed: a refusal where acceptance would be better. Never a throw,
    // the twin touched, TWIN bytes written, or a write outside the case's folder.
    private static readonly Dictionary<string, string> KnownFindings = [with(StringComparer.Ordinal)];

    private readonly TempDirectory _temp = new();
    private readonly PakkoServices _services = PakkoServices.Create(new GroupPolicyOptions());
    private readonly HashSet<string> _seen = [with(StringComparer.Ordinal)];

    public void Dispose()
    {
        if (Directory.Exists(_temp.Path))
            Directory.Delete(Literal + _temp.Path, recursive: true);
    }

    private sealed record Case(NameShape Shape, PathForm Form, ShapePosition Position, bool Twin, Role Role, Engine Engine)
    {
        public string Id => $"{Role}:{Engine}:{Shape.Id}:{Position}:{Form.Id}:{(Twin ? "twin" : "alone")}";
    }

    private static List<Case> Cases(IReadOnlyList<NameShape> allShapes)
    {
        Role[] roles = Enum.GetValues<Role>();
        Engine[] engines = Enum.GetValues<Engine>();
        ShapePosition[] positions = Enum.GetValues<ShapePosition>();
        (NameShape[] shapes, List<int[]> pairs) = Pairs(allShapes, roles.Length);
        return [.. pairs.Select(c => new Case(
            shapes[c[0]], PathForms.All[c[1]], positions[c[2]], c[3] == 1, roles[c[4]], engines[c[5]]))];
    }

    // All pairs of shape, form, position, twin, role and engine (ZIP or tar.gz). "." and ".." name the
    // folder itself and its parent, not a place in it: the pure tests take them.
    private static (NameShape[] Shapes, List<int[]> Pairs) Pairs(IReadOnlyList<NameShape> allShapes, int roles)
    {
        NameShape[] shapes = [.. allShapes.Where(s => s.Name is not ("." or ".."))];
        ShapePosition[] positions = Enum.GetValues<ShapePosition>();
        int[] sizes = [shapes.Length, PathForms.All.Count, positions.Length, 2, roles, 2];
        // A twin needs a name Windows trims to another at that position; with the position unset,
        // at either. Pairwise in effect: every pair it allows extends to a whole case.
        bool Allowed(int?[] p)
        {
            if (p[3] != 1 || p[0] is not { } shape)
                return true;
            return p[2] is { } position
                ? Trims(shapes[shape], positions[position])
                : positions.Any(at => Trims(shapes[shape], at));
        }
        return (shapes, AllPairs.Generate(sizes, Allowed));
    }

    // Windows opens another name in the same folder: a twin can stand there.
    private static bool Trims(NameShape shape, ShapePosition position)
    {
        if (!shape.OnDisk)
            return false;
        string written = PathForms.Absolute.Spell(@"C:\pm\x", PathForms.Parts(shape, position));
        string opened = Path.GetFullPath(written);
        // "..." trims to nothing: Windows opens the folder itself, not a name beside it.
        return !string.Equals(opened, written, StringComparison.Ordinal)
            && string.Equals(Path.GetPathRoot(opened), @"C:\", StringComparison.Ordinal)
            && opened.Count(ch => ch == '\\') == written.Count(ch => ch == '\\') && !opened.EndsWith('\\');
    }

    [Fact]
    public async Task QuickShapes_AllPairs_NeverReachTheTwinNorLeaveTheFolder()
    {
        List<Case> cases = Cases(NameShapes.Quick);
        var failures = new List<string>();
        for (int i = 0; i < cases.Count; i++)
            failures.AddRange(await RunAsync(cases[i], i));
        Report(failures, cases.Count);
    }

    [Fact, Trait("Category", "Fuzz")]
    public async Task AllShapes_SampledPairs_NeverReachTheTwinNorLeaveTheFolder()
    {
        List<Case> cases = Cases(NameShapes.All);
        int iterations = ReadInt("PAKKO_FUZZ_ITERATIONS") ?? 10;
        int seed = ReadInt("PAKKO_FUZZ_SEED") ?? 20260929;
        int? only = ReadInt("PAKKO_FUZZ_ONLY_ITERATION");
        var failures = new List<string>();
        // Every case once when the budget covers them all, else a seeded sample.
        for (int iteration = 0; iteration < Math.Min(iterations, cases.Count); iteration++)
        {
            if (only is not null && only != iteration)
                continue;
            int index = iterations >= cases.Count ? iteration : new Random(unchecked(seed * 486187739 ^ iteration)).Next(cases.Count);
            foreach (string failure in await RunAsync(cases[index], index))
                failures.Add($"{failure} (PAKKO_FUZZ_SEED={seed} PAKKO_FUZZ_ONLY_ITERATION={iteration})");
        }
        Report(failures, cases.Count);
    }

    // T-F512 (c): the same pairs for the installed pakko (CLI-056, on Windows 11 and on the PakkoW10
    // VM, which has no dotnet). The list is kept in the repo and regenerated here: a test run with
    // PAKKO_PATHMATRIX_WRITE=1 rewrites it, any other run fails when it differs from the generator.
    private static readonly string[] CliRoles = ["a-source", "a-sdel", "x-archive", "x-destination"];

    [Fact]
    public void CliCases_TheSmokeListIsTheGeneratorsOwn()
    {
        string file = Path.Combine(ArchiveHelperBuild.RepoRoot, "scripts", "smoke", "PathMatrixCases.json");
        string generated = CliCasesJson();
        if (Environment.GetEnvironmentVariable("PAKKO_PATHMATRIX_WRITE") == "1")
            File.WriteAllText(file, generated);
        File.ReadAllText(file).ReplaceLineEndings("\n").Should().Be(generated, "run the tests once with PAKKO_PATHMATRIX_WRITE=1 after a change to the shapes or forms");
    }

    // Paths are spelled under Z:\R (the smoke step puts its own folder there, and \\localhost\Z$\R
    // for the share); a relative path is relative to Z:\R, where the step runs pakko.
    private static string CliCasesJson()
    {
        const string root = @"Z:\R";
        (NameShape[] shapes, List<int[]> pairs) = Pairs(NameShapes.Quick, CliRoles.Length);
        ShapePosition[] positions = Enum.GetValues<ShapePosition>();
        var lines = new List<string>();
        for (int i = 0; i < pairs.Count; i++)
        {
            int[] c = pairs[i];
            NameShape shape = shapes[c[0]];
            PathForm form = PathForms.All[c[1]];
            ShapePosition position = positions[c[2]];
            bool twin = c[3] == 1;
            string folder = root + "\\c" + i;
            string under = form.Id == "over-260" ? folder + "\\" + new string('p', 250) : folder;
            IReadOnlyList<string> parts = PathForms.Parts(shape, position);
            string place = PathForms.Absolute.Spell(under, parts);
            string path = form.Id == "relative" ? PathForms.Absolute.Spell("c" + i, parts) : form.Spell(folder, parts);
            var item = new Dictionary<string, object?>
            {
                ["id"] = $"{CliRoles[c[4]]}:{(c[5] == 0 ? "zip" : "tar.gz")}:{shape.Id}:{position}:{form.Id}:{(twin ? "twin" : "alone")}",
                ["role"] = CliRoles[c[4]],
                ["ext"] = c[5] == 0 ? "zip" : "tar.gz",
                ["plain"] = shape.Id == "plain",
                ["onDisk"] = shape.OnDisk,
                ["path"] = path,
                ["place"] = place,
                ["twin"] = twin ? Path.GetFullPath(place) : null,
            };
            lines.Add("  " + System.Text.Json.JsonSerializer.Serialize(item));
        }
        return "[\n" + string.Join(",\n", lines) + "\n]\n";
    }

    private async Task<List<string>> RunAsync(Case c, int index)
    {
        var failures = new List<string>();
        string folder = Path.Combine(_temp.Path, "c" + index);
        Directory.CreateDirectory(folder);
        _seen.Add("c" + index);
        // The over-260 form puts the shape under a longer folder: the place it names is there.
        string under = c.Form.Id == "over-260" ? Path.Combine(folder, new string('p', 250)) : folder;
        IReadOnlyList<string> parts = PathForms.Parts(c.Shape, c.Position);
        string place = PathForms.Absolute.Spell(under, parts);
        string twinPlace = Path.GetFullPath(place);
        string literalMark = "LITERAL-" + index;
        string twinMark = TwinMark + index;

        Build(c, place, literalMark, isTwin: false);
        if (c.Twin)
            Build(c, twinPlace, twinMark, isTwin: true);
        string twinBefore = c.Twin ? StateOf(Literal + twinPlace) : "";
        string path = c.Form.Spell(folder, parts);
        string output = Path.Combine(folder, "out");

        ArchiveResult? result;
        try
        {
            using var cts = new CancellationTokenSource(CaseTimeout);
            result = await Run(c, folder, path, output, literalMark, cts.Token).WaitAsync(CaseTimeout + TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            failures.Add($"{c.Id} ({Show(path)}) threw {ex.GetType().Name}: {ex.Message}");
            return failures;
        }

        if (c.Twin && StateOf(Literal + twinPlace) != twinBefore)
            failures.Add($"{c.Id} ({Show(path)}): the twin {Show(twinPlace)} changed");
        string written;
        try
        {
            written = WrittenText(c, result, place, output);
        }
        catch (IOException ex)
        {
            failures.Add($"{c.Id} ({Show(path)}): what it reports as written does not open: {ex.Message}");
            return failures;
        }
        if (written.Contains(TwinMark, StringComparison.Ordinal))
            failures.Add($"{c.Id} ({Show(path)}): the twin's bytes were written");
        bool clean = result.Errors.Count == 0;
        if (clean && c.Shape.OnDisk && !written.Contains(literalMark, StringComparison.Ordinal))
            failures.Add($"{c.Id} ({Show(path)}): a clean result without the named place's bytes");
        if (c.Shape.Id == "plain" && !clean)
            failures.Add($"{c.Id} ({Show(path)}): the plain name was refused: {string.Join("; ", result.Errors.Select(e => e.Text?.Code.ToString() ?? e.Message))}");
        string? outside = Outside(c, folder, parts, twinPlace);
        if (outside is not null)
            failures.Add($"{c.Id} ({Show(path)}): {outside}");
        return failures;
    }

    // The named place (or its twin) as the role needs it: a file, an archive, or for a destination
    // nothing at the place and a folder with a marked file at the twin.
    private static void Build(Case c, string at, string mark, bool isTwin)
    {
        if (!c.Shape.OnDisk)
            return;
        Directory.CreateDirectory(Literal + Path.GetDirectoryName(at));
        switch (c.Role)
        {
            case Role.CreateSource:
                File.WriteAllText(Literal + at, mark);
                break;
            case Role.ExtractArchive:
                File.WriteAllBytes(Literal + at, ArchiveOf(c.Engine, mark));
                break;
            default:
                if (isTwin)
                {
                    Directory.CreateDirectory(Literal + at);
                    File.WriteAllText(Literal + Path.Combine(at, "t.txt"), mark);
                }
                break;
        }
    }

    private Task<ArchiveResult> Run(Case c, string folder, string path, string output, string literalMark, CancellationToken token)
    {
        string fixedSource = Path.Combine(folder, "src.txt");
        string fixedArchive = Path.Combine(folder, c.Engine == Engine.Zip ? "in.zip" : "in.tar.gz");
        ArchiveContainerFormat format = c.Engine == Engine.Zip ? ArchiveContainerFormat.Zip : ArchiveContainerFormat.TarGz;
        switch (c.Role)
        {
            case Role.CreateSource:
                return _services.CreationRouter.ArchiveAsync(new ArchiveOptions
                {
                    SourcePaths = [path], DestinationFolder = output, ArchiveName = "x", Format = format,
                }, cancellationToken: token);
            case Role.CreateDestination:
                File.WriteAllText(fixedSource, literalMark);
                return _services.CreationRouter.ArchiveAsync(new ArchiveOptions
                {
                    SourcePaths = [fixedSource], DestinationFolder = path, ArchiveName = "x", Format = format,
                }, cancellationToken: token);
            case Role.ExtractArchive:
                return Extract(c.Engine, path, output, token);
            default:
                File.WriteAllBytes(fixedArchive, ArchiveOf(c.Engine, literalMark));
                return Extract(c.Engine, fixedArchive, path, token);
        }
    }

    private Task<ArchiveResult> Extract(Engine engine, string archive, string destination, CancellationToken token)
    {
        var options = new ExtractOptions { ArchivePaths = [archive], DestinationFolder = destination };
        return engine == Engine.Zip
            ? _services.ArchiveService.ExtractAsync(options, cancellationToken: token)
            : _services.TarService.ExtractAsync(options, cancellationToken: token);
    }

    // The text of everything the operation wrote: the archives it created, or the files it extracted.
    private static string WrittenText(Case c, ArchiveResult result, string place, string output)
    {
        var text = new StringBuilder();
        foreach (string archive in result.CreatedFiles.Where(File.Exists))
            text.Append(ArchiveText(archive));
        string extractedInto = c.Role switch
        {
            Role.ExtractArchive => output,
            Role.ExtractDestination => place,
            _ => "",
        };
        if (extractedInto.Length > 0 && Directory.Exists(Literal + extractedInto))
            foreach (string file in Directory.GetFiles(Literal + extractedInto, "*", SearchOption.AllDirectories))
                text.Append(File.ReadAllText(file));
        if (c.Role == Role.CreateDestination && Directory.Exists(Literal + place))
            foreach (string file in Directory.GetFiles(Literal + place, "*", SearchOption.AllDirectories))
                text.Append(ArchiveText(file));
        return text.ToString();
    }

    private static string ArchiveText(string archive)
    {
        // As reported: a path Pakko says it created must open as that file.
        using FileStream file = File.OpenRead(archive);
        if (archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var zip = new ZipArchive(file, ZipArchiveMode.Read);
            var text = new StringBuilder();
            foreach (ZipArchiveEntry entry in zip.Entries.Where(e => !e.FullName.EndsWith('/')))
            {
                using var reader = new StreamReader(entry.Open());
                text.Append(reader.ReadToEnd());
            }
            return text.ToString();
        }
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var bytes = new MemoryStream();
        gzip.CopyTo(bytes);
        return Encoding.Latin1.GetString(bytes.ToArray());
    }

    private static byte[] ArchiveOf(Engine engine, string mark)
    {
        using var bytes = new MemoryStream();
        if (engine == Engine.Zip)
        {
            using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
            using (var writer = new StreamWriter(zip.CreateEntry("m.txt").Open()))
                writer.Write(mark);
            return bytes.ToArray();
        }
        using (var gzip = new GZipStream(bytes, CompressionLevel.Fastest, leaveOpen: true))
        using (var tar = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true))
        {
            var entry = new PaxTarEntry(TarEntryType.RegularFile, "m.txt") { DataStream = new MemoryStream(Encoding.ASCII.GetBytes(mark)) };
            tar.WriteEntry(entry);
        }
        return bytes.ToArray();
    }

    // Anything new outside the case's folder, or at its top a name the case does not explain.
    private string? Outside(Case c, string folder, IReadOnlyList<string> parts, string twinPlace)
    {
        string[] top = [.. Directory.GetFileSystemEntries(Literal + _temp.Path).Select(Path.GetFileName)!];
        // Each stray is reported once, by the case that wrote it.
        string[] stray = [.. top.Where(name => _seen.Add(name!))!];
        if (stray.Length > 0)
            return "written outside the case's folder: " + string.Join(", ", stray.Select(s => Show(s!)));
        var expected = new HashSet<string>(StringComparer.Ordinal) { "out", "src.txt", "in.zip", "in.tar.gz", new string('p', 250), parts[0] };
        if (c.Twin)
            expected.Add(Path.GetFileName(Path.GetRelativePath(folder, twinPlace).Split('\\')[0]));
        string[] unexplained = [.. Directory.GetFileSystemEntries(Literal + folder).Select(Path.GetFileName).Where(name => !expected.Contains(name!))!];
        return unexplained.Length > 0 ? "an unexplained name in the case's folder: " + string.Join(", ", unexplained.Select(s => Show(s!))) : null;
    }

    private static string StateOf(string path)
    {
        if (File.Exists(path))
            return Convert.ToHexString(File.ReadAllBytes(path)) + "@" + File.GetLastWriteTimeUtc(path).Ticks;
        if (!Directory.Exists(path))
            return "gone";
        return string.Join("|", Directory.GetFileSystemEntries(path, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(entry => entry + "=" + (File.Exists(entry) ? StateOf(entry) : "dir")));
    }

    private static void Report(List<string> failures, int cases)
    {
        string[] unknown = [.. failures.Where(f => !KnownFindings.Keys.Any(k => f.StartsWith(k + " ", StringComparison.Ordinal) || f.StartsWith(k + ":", StringComparison.Ordinal)))];
        string.Join(Environment.NewLine, unknown).Should().BeEmpty("each line is a case an operation gets wrong ({0} of {1} cases)", unknown.Length, cases);
    }

    private static int? ReadInt(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } raw ? int.Parse(raw, System.Globalization.CultureInfo.InvariantCulture) : null;

    private static string Show(string text) =>
        "\"" + string.Concat(text.Select(ch => char.IsControl(ch) || char.IsSurrogate(ch) || ch is (char)0x200B or (char)0x202E or (char)0x301
            ? $"<U+{(int)ch:X4}>" : ch.ToString())) + "\"";
}
