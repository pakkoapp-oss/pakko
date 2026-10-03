using System.Xml.Linq;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F317: the MSIX carries pakko.exe as its own hidden Application with the "pakko.exe" execution
// alias, and Archiver.App.csproj packages the four files its apphost needs (the runtime itself is
// the App's, at the package root). CI-Build-Msix.ps1 checks the same in the built package.
public sealed class PackagingManifestTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [Fact]
    public void Manifest_DeclaresPakkoAsAHiddenFullTrustApplicationWithItsAlias()
    {
        var manifest = XDocument.Load(Path.Combine(RepoRoot, "src", "Archiver.App", "Package.appxmanifest"));
        List<XElement> cli = [.. manifest.Descendants()
            .Where(e => e.Name.LocalName == "Application" && (string?)e.Attribute("Executable") == "pakko.exe")];

        cli.Should().ContainSingle();
        XElement app = cli[0];
        ((string?)app.Attribute("EntryPoint")).Should().Be("Windows.FullTrustApplication");
        app.Descendants().Single(e => e.Name.LocalName == "VisualElements")
            .Attribute("AppListEntry")?.Value.Should().Be("none");

        XElement extension = app.Descendants().Single(e => e.Name.LocalName == "Extension");
        ((string?)extension.Attribute("Category")).Should().Be("windows.appExecutionAlias");
        ((string?)extension.Attribute("Executable")).Should().Be("pakko.exe");
        app.Descendants().Where(e => e.Name.LocalName == "ExecutionAlias")
            .Select(e => (string?)e.Attribute("Alias")).Should().Equal("pakko.exe");

        manifest.Descendants().Where(e => e.Name.LocalName == "ExecutionAlias")
            .Should().ContainSingle("no other application may claim an alias");
    }

    [Fact]
    public void AppProject_PackagesPakkosApphostFiles()
    {
        var project = XDocument.Load(Path.Combine(RepoRoot, "src", "Archiver.App", "Archiver.App.csproj"));
        List<(string Include, string Link)> pakko = [.. project.Descendants()
            .Where(e => e.Name.LocalName == "Content")
            .Select(e => ((string?)e.Attribute("Include") ?? "", e.Elements().FirstOrDefault(c => c.Name.LocalName == "Link")?.Value ?? ""))
            .Where(c => c.Item2.StartsWith("pakko.", StringComparison.Ordinal))];

        pakko.Select(c => c.Link).Should().BeEquivalentTo(
            ["pakko.exe", "pakko.dll", "pakko.deps.json", "pakko.runtimeconfig.json"]);
        pakko.Should().OnlyContain(c => c.Include.StartsWith(@"..\Archiver.CLI\bin\", StringComparison.Ordinal)
            && c.Include.EndsWith(@"\" + c.Link, StringComparison.Ordinal));
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "windows-archiver-wrapper.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }
}
