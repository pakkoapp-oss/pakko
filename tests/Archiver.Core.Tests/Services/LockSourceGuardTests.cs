using System.Text.RegularExpressions;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F363: a lock is a System.Threading.Lock, not a bare object - the type says what it is for,
// `lock` on it takes the cheaper Lock.EnterScope path, and passing it as an object is a compiler
// warning (an error here). A bare `object x = new()` exists in this code only to be locked on.
public sealed partial class LockSourceGuardTests
{
    [Fact]
    public void NoBareObjectIsCreatedToLockOn()
    {
        string root = Path.Combine(FindRepoRoot(), "src");
        List<string> offenders = [];
        foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(root, file);
            if (relative.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || relative.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                continue;

            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                if (BareObject().IsMatch(lines[i]))
                    offenders.Add($"{relative}:{i + 1}: {lines[i].Trim()}");
            }
        }

        offenders.Should().BeEmpty();
    }

    [GeneratedRegex(@"\bobject\s+\w+\s*=\s*new(\s+object)?\s*\(\s*\)")]
    private static partial Regex BareObject();

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "windows-archiver-wrapper.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }
}
