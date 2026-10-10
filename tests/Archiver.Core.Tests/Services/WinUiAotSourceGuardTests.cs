using System.Text.RegularExpressions;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F355: under Native AOT a value read from a WinUI resource dictionary is a WinRT object that a
// plain (Style)/(Brush) cast cannot unwrap - InvalidCastException at runtime, and `as T` silently
// gives null. dotnet test runs under JIT and never sees it, so this reads the WinUI sources instead.
public sealed class WinUiAotSourceGuardTests
{
    private static readonly string[] ResourceReads = ["Resources[", "ThemeDictionaries", "Resources.TryGetValue"];

    [Theory]
    [InlineData("Archiver.App")]
    [InlineData("Archiver.OperationUi")]
    public void ResourceDictionaryReads_GoThroughCastExtensionsAs(string project)
    {
        string root = Path.Combine(FindRepoRoot(), "src", project);
        List<string> offenders = [];
        foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(root, file);
            if (relative.StartsWith("bin", StringComparison.Ordinal) || relative.StartsWith("obj", StringComparison.Ordinal))
                continue;

            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                if (ResourceReads.Any(r => lines[i].Contains(r, StringComparison.Ordinal))
                    && !lines[i].Contains("WinRT.CastExtensions.As<", StringComparison.Ordinal))
                    offenders.Add($"{relative}:{i + 1}: {lines[i].Trim()}");
            }
        }

        offenders.Should().BeEmpty();
    }

    // T-F275: a property typed as an interface (IReadOnlyList<string>) and built from a collection
    // expression crashed the deployed App at startup in Microsoft.UI.Xaml.dll (0xc000027b) - WinRT
    // cannot marshal the compiler's own list type. A property an ItemsSource binds to is declared
    // as a concrete collection, so "= []" builds that collection.
    [Fact]
    public void ItemsSourceProperties_HaveAConcreteCollectionType()
    {
        string app = Path.Combine(FindRepoRoot(), "src", "Archiver.App");
        string xaml = File.ReadAllText(Path.Combine(app, "MainWindow.xaml"));
        string viewModel = File.ReadAllText(Path.Combine(app, "ViewModels", "MainViewModel.cs"));
        string[] bound = [.. Regex.Matches(xaml, @"ItemsSource=""\{x:Bind ViewModel\.(\w+)").Select(m => m.Groups[1].Value)];
        bound.Should().Contain("RecoveryPercentChoices", "the guard must read the bindings it is about");

        List<string> offenders = [];
        foreach (string property in bound)
        {
            Match declaration = Regex.Match(viewModel, @"public\s+(?:partial\s+)?(\S+)\s+" + property + @"\s*(?:\{|=>)");
            declaration.Success.Should().BeTrue(property);
            if (Regex.IsMatch(declaration.Groups[1].Value, @"^I[A-Z]\w*<"))
                offenders.Add($"{property}: {declaration.Groups[1].Value}");
        }

        offenders.Should().BeEmpty();
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "windows-archiver-wrapper.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }
}
