using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.Tests.PathMatrix;

/// <summary>
/// T-F512: the owners' pure rules over the full product of name shapes, path forms and positions,
/// judged by Windows itself (<see cref="Path.GetFullPath(string)"/>) where Windows is the rule, and by
/// the rule Pakko chose where it chose one (the 7-Zip rule for entry names). Each test collects every
/// failing case and reports them together. A finding that is known and allowed (a safe refusal, see
/// <see cref="KnownFindings"/>) is listed with its task; a new one fails, and so does a listed one
/// that is gone.
/// </summary>
public sealed class PathMatrixPureTests
{
    private static readonly string Folder = Path.Combine(Path.GetPathRoot(Environment.CurrentDirectory)!, "pm", "x");

    // Only safe outcomes may be listed here: a different refusal, or a refusal where acceptance
    // would be better. Never a throw, another file's bytes, or a write outside the destination.
    // Empty: every finding so far was fixed in its owner.
    private static readonly Dictionary<string, string> KnownFindings = new(StringComparer.Ordinal);

    private enum WindowsOutcome { Unchanged, Trimmed, Device, Step }

    // How Win32 opens the shape at the position: as written, with its name trimmed, or as a device.
    private static WindowsOutcome WindowsOpens(NameShape shape, ShapePosition position)
    {
        if (shape.Name is "." or "..")
            return WindowsOutcome.Step;
        string written = PathForms.Absolute.Spell(Folder, PathForms.Parts(shape, position));
        string opened = Path.GetFullPath(written);
        if (string.Equals(opened, written, StringComparison.Ordinal))
            return WindowsOutcome.Unchanged;
        return string.Equals(Path.GetPathRoot(opened), Path.GetPathRoot(written), StringComparison.Ordinal)
            ? WindowsOutcome.Trimmed
            : WindowsOutcome.Device;
    }

    [Fact]
    public void InputPathsProblem_RefusesExactlyTheNamesWindowsTrims_InEverySpelling()
    {
        var failures = new List<string>();
        foreach (NameShape shape in NameShapes.All)
            foreach (ShapePosition position in Enum.GetValues<ShapePosition>())
            {
                WindowsOutcome windows = WindowsOpens(shape, position);
                foreach (PathForm form in PathForms.All)
                {
                    string path = form.Spell(Folder, PathForms.Parts(shape, position));
                    string id = $"Problem:{shape.Id}:{position}:{form.Id}";
                    MessageCode? code;
                    try
                    {
                        code = InputPaths.Problem(path)?.Code;
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"{id} threw {ex.GetType().Name}: {ex.Message}");
                        continue;
                    }
                    bool trimmedRefusal = code == MessageCode.SourceNameTrimmed;
                    string? wrong = windows switch
                    {
                        WindowsOutcome.Trimmed when !trimmedRefusal => $"Windows trims it, but Problem gave {code?.ToString() ?? "no refusal"}",
                        WindowsOutcome.Unchanged when trimmedRefusal => "Windows keeps it, but Problem refused it as trimmed",
                        _ => null,
                    };
                    if (wrong is not null)
                        failures.Add($"{id} ({Show(path)}): {wrong}");
                }
            }
        Report(failures);
    }

    [Fact]
    public void SingleName_AndGetFolderName_GiveANameWindowsKeeps()
    {
        var failures = new List<string>();
        foreach (NameShape shape in NameShapes.All)
        {
            Check($"SingleName:{shape.Id}", EntryDiskName.SingleName(shape.Name), failures);
            foreach (string extension in new[] { ".zip", ".tar.gz", ".7z" })
            {
                string archive = Folder + "\\" + shape.Name + extension;
                string folder;
                try
                {
                    folder = ArchiveNaming.GetFolderName(archive);
                }
                catch (Exception ex)
                {
                    failures.Add($"GetFolderName:{shape.Id}{extension} threw {ex.GetType().Name}: {ex.Message}");
                    continue;
                }
                Check($"GetFolderName:{shape.Id}{extension}", folder, failures);
            }
        }
        Report(failures);

        static void Check(string id, string name, List<string> failures)
        {
            string written = Folder + "\\" + name;
            string? wrong =
                name.Length == 0 ? "empty"
                : name is "." or ".." ? "a step, not a name"
                : name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ? "holds a character Windows refuses or a separator"
                : !string.Equals(Path.GetFullPath(written), written, StringComparison.Ordinal) ? "Windows opens another name"
                // Windows 10 and Server 2022 open these as devices, "CON.txt" too (T-F475).
                : ArchiveEntrySecurity.HasReservedName(name) ? "a device name on Windows 10"
                : !string.Equals(EntryDiskName.SingleName(name), name, StringComparison.Ordinal) ? "not stable: named again it changes"
                : null;
            if (wrong is not null)
                failures.Add($"{id} -> {Show(name)}: {wrong}");
        }
    }

    // The 7-Zip rule Pakko chose for entry names (T-F420): every part on its own, its trailing dots
    // and spaces gone, a refused character '_', an empty part '_'; ".." stays a step for the
    // containment check after it. Not Windows' rule: Windows keeps "d " in the middle of a path.
    // Outside its contract: ':' and control characters (the entry is refused before, T-F38/T-F39).
    // A name of steps only ("." or "./") is the destination itself, so it is written as "".
    [Fact]
    public void AsPakkoWrites_FollowsThe7ZipRule_ForEveryPartAndPosition()
    {
        var failures = new List<string>();
        foreach (NameShape shape in NameShapes.All)
            foreach (ShapePosition position in Enum.GetValues<ShapePosition>())
            {
                string name = string.Join('/', PathForms.Parts(shape, position));
                if (name.Any(c => c == ':' || c < 0x20))
                    continue;
                string id = $"AsPakkoWrites:{shape.Id}:{position}";
                string written = EntryDiskName.AsPakkoWrites(name);
                if (shape.Name == "." && position == ShapePosition.Last)
                {
                    if (written.Length != 0)
                        failures.Add($"{id} -> {Show(written)}: a name of steps only is not the destination itself");
                    continue;
                }
                foreach (string part in written.Split('/'))
                {
                    string? wrong =
                        part.Length == 0 ? "an empty part"
                        : part == ".." ? null
                        : part.EndsWith(' ') || part.EndsWith('.') ? "a part still ends in a space or a dot"
                        : part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ? "a part holds a character Windows refuses"
                        : null;
                    if (wrong is not null)
                        failures.Add($"{id} -> {Show(written)}: {wrong}");
                }
                if (!string.Equals(EntryDiskName.AsPakkoWrites(written), written, StringComparison.Ordinal))
                    failures.Add($"{id} -> {Show(written)}: not stable");
                bool trimsAPart = name.Split('/').Any(p => p is not ("." or "..") && p.Length > 0 && (p.EndsWith(' ') || p.EndsWith('.')));
                if (EntryDiskName.Trims(name) != trimsAPart)
                    failures.Add($"{id}: Trims says {EntryDiskName.Trims(name)}");
            }
        Report(failures);
    }

    [Fact]
    public void IsDevice_NeverThrows()
    {
        var failures = new List<string>();
        foreach (NameShape shape in NameShapes.All)
            foreach (ShapePosition position in Enum.GetValues<ShapePosition>())
                foreach (PathForm form in PathForms.All)
                {
                    string path = form.Spell(Folder, PathForms.Parts(shape, position));
                    try
                    {
                        DestinationFolder.IsDevice(path);
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"IsDevice:{shape.Id}:{position}:{form.Id} threw {ex.GetType().Name}: {ex.Message}");
                    }
                }
        Report(failures);
    }

    // A known finding that still fails is skipped; one that no longer fails is reported, so the
    // list only shrinks with the code.
    private static void Report(List<string> failures)
    {
        string[] unknown = [.. failures.Where(f => !KnownFindings.Keys.Any(k => f.StartsWith(k + " ", StringComparison.Ordinal) || f.StartsWith(k + ":", StringComparison.Ordinal)))];
        string.Join(Environment.NewLine, unknown).Should().BeEmpty("each line is a name or path the owner gets wrong ({0} in all)", unknown.Length);
    }

    // Control and invisible characters as code points, so a failure message shows them.
    private static string Show(string text) =>
        "\"" + string.Concat(text.Select(c => char.IsControl(c) || char.IsSurrogate(c) || c is (char)0x200B or (char)0x202E or (char)0x301
            ? $"<U+{(int)c:X4}>" : c.ToString())) + "\"";
}
