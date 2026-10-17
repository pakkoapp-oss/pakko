using System.Text.RegularExpressions;
using FluentAssertions;

namespace Archiver.App.Core.Tests;

// T-F406: an exception from an async void method has no caller to reach; WinUI does not reliably
// hand it to Application.UnhandledException either. Every async void in the GUI programs catches
// inside its own body (App.ReportUnexpected logs it and tells the user). A new one without a catch
// fails here.
public sealed partial class AsyncVoidHandlersTests
{
    private static readonly string[] GuiProjects = ["Archiver.App", "Archiver.OperationUi"];

    [Fact]
    public void EveryAsyncVoidMethod_CatchesInsideItsBody()
    {
        string root = FindRepoRoot();
        string[] files = [.. GuiProjects
            .SelectMany(project => Directory.EnumerateFiles(Path.Combine(root, "src", project), "*.cs", SearchOption.AllDirectories))
            .Where(IsSource)];

        var found = new List<string>();
        var withoutCatch = new List<string>();
        foreach (string file in files)
        {
            string code = File.ReadAllText(file);
            foreach (Match match in AsyncVoid().Matches(code))
            {
                string name = $"{Path.GetFileName(file)}: {match.Groups[1].Value}";
                found.Add(name);
                if (!Body(code, match.Index + match.Length).Contains("catch (", StringComparison.Ordinal))
                    withoutCatch.Add(name);
            }
        }

        found.Should().NotBeEmpty("the App has async void event handlers; the pattern must still find them");
        withoutCatch.Should().BeEmpty();
    }

    // The method body: from the first brace after the signature to its matching brace.
    private static string Body(string code, int from)
    {
        int open = code.IndexOf('{', from);
        int depth = 0;
        for (int i = open; i >= 0 && i < code.Length; i++)
        {
            if (code[i] == '{')
                depth++;
            else if (code[i] == '}' && --depth == 0)
                return code[open..(i + 1)];
        }
        return "";
    }

    private static bool IsSource(string path) =>
        !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "windows-archiver-wrapper.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }

    [GeneratedRegex(@"\basync\s+void\s+(\w+)\s*\(")]
    private static partial Regex AsyncVoid();
}
