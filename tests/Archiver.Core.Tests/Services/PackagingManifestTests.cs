using System.Xml.Linq;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F317: the MSIX carries pakko.exe as its own hidden Application with the "pakko.exe" execution
// alias. T-F355: every exe is Native AOT, so each satellite is packaged as its exe alone - there is
// no .NET runtime at the package root for an apphost to find. CI-Build-Msix.ps1 checks the built package.
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

    // T-F368: the Store rejects a nonzero revision; Deploy.ps1 puts the dev revision in a generated
    // copy under obj\, never in the tracked file.
    [Fact]
    public void Manifest_KeepsTheRevisionAtZero()
    {
        var manifest = XDocument.Load(Path.Combine(RepoRoot, "src", "Archiver.App", "Package.appxmanifest"));
        string version = (string)manifest.Descendants().Single(e => e.Name.LocalName == "Identity").Attribute("Version")!;

        Version.Parse(version).Revision.Should().Be(0);
    }

    [Fact]
    public void AppProject_PackagesPakkoAsItsExeAlone()
    {
        List<(string Include, string Link)> pakko = [.. SatelliteContent().Where(c => c.Link.StartsWith("pakko.", StringComparison.Ordinal))];

        pakko.Select(c => c.Link).Should().Equal("pakko.exe");
        pakko.Should().OnlyContain(c => c.Include.StartsWith(@"..\Archiver.CLI\bin\", StringComparison.Ordinal)
            && c.Include.EndsWith(@"\" + c.Link, StringComparison.Ordinal));
    }

    [Fact]
    public void AppProject_PackagesEachSatelliteAsItsExeAlone()
    {
        SatelliteContent().Select(c => c.Link).Should().BeEquivalentTo(
            ["Archiver.Shell.exe", "Archiver.OperationUi.exe", "pakko.exe"]);
    }

    [Theory]
    [InlineData("Archiver.App")]
    [InlineData("Archiver.Shell")]
    [InlineData("Archiver.OperationUi")]
    [InlineData("Archiver.CLI")]
    public void ExeProject_PublishesNativeAot(string name)
    {
        XDocument project = LoadProject(name);

        Property(project, "PublishAot").Should().Equal("true");
        Property(project, "PublishReadyToRun").Should().BeEmpty("ReadyToRun is a JIT-runtime format, meaningless under AOT");
        Property(project, "PublishTrimmed").Should().BeEmpty();
    }

    [Theory]
    [InlineData("Archiver.App")]
    [InlineData("Archiver.OperationUi")]
    public void WinUiProject_EnablesTheCsWinRtAotChecks(string name)
    {
        XDocument project = LoadProject(name);

        Property(project, "AllowUnsafeBlocks").Should().Equal("true");
        Property(project, "CsWinRTAotWarningLevel").Should().Equal("2");
    }

    [Theory]
    [InlineData("Archiver.Core")]
    [InlineData("Archiver.App.Core")]
    [InlineData("Archiver.Messages")]
    [InlineData("Archiver.OperationUi.Core")]
    [InlineData("Archiver.OperationUi.Protocol")]
    public void Library_IsAotCompatible(string name)
    {
        Property(LoadProject(name), "IsAotCompatible").Should().Equal("true");
    }

    [Fact]
    public void Repo_PinsCSharp14Once()
    {
        Property(XDocument.Load(Path.Combine(RepoRoot, "Directory.Build.props")), "LangVersion").Should().Equal("14");

        string[] overriding = [.. Directory.EnumerateFiles(Path.Combine(RepoRoot, "src"), "*.csproj", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(RepoRoot, "tests"), "*.csproj", SearchOption.AllDirectories))
            .Where(p => Property(XDocument.Load(p), "LangVersion").Count > 0)
            .Select(Path.GetFileName)!];
        overriding.Should().BeEmpty("the language version is set once, in Directory.Build.props (T-F363)");
    }

    [Theory]
    [InlineData("Microsoft.WindowsAppSDK", "2.5.1")]
    [InlineData("Microsoft.Windows.SDK.BuildTools", "10.0.28000.2705")]
    [InlineData("Microsoft.Windows.AI.MachineLearning", "2.1.74")]
    public void WinUiProjects_ReferenceTheSamePackageVersion(string package, string version)
    {
        // OperationUi runs against the WindowsAppRuntime framework App's manifest declares (T-F363).
        foreach (string name in WinUiProjects)
            LoadProject(name).Descendants()
                .Where(e => e.Name.LocalName == "PackageReference" && (string?)e.Attribute("Include") == package)
                .Select(e => (string?)e.Attribute("Version"))
                .Should().Equal([version], name);
    }

    [Fact]
    public void WinUiProjects_ExcludeTheWindowsMlNativeDlls()
    {
        // 2.x's metapackage publishes onnxruntime/DirectML/AI.MachineLearning DLLs (16 MB) Pakko never calls (T-F363).
        foreach (string name in WinUiProjects)
            LoadProject(name).Descendants()
                .Where(e => e.Name.LocalName == "PackageReference" && (string?)e.Attribute("Include") == "Microsoft.Windows.AI.MachineLearning")
                .Select(e => (string?)e.Attribute("ExcludeAssets"))
                .Should().Equal(["native"], name);
    }

    private static readonly string[] WinUiProjects = ["Archiver.App", "Archiver.OperationUi"];

    private static List<(string Include, string Link)> SatelliteContent() =>
        [.. LoadProject("Archiver.App").Descendants()
            .Where(e => e.Name.LocalName == "Content")
            .Select(e => ((string?)e.Attribute("Include") ?? "", e.Elements().FirstOrDefault(c => c.Name.LocalName == "Link")?.Value ?? ""))
            .Where(c => SatelliteProjects.Any(p => c.Item1.StartsWith(@"..\" + p + @"\bin\", StringComparison.Ordinal)))];

    private static readonly string[] SatelliteProjects = ["Archiver.Shell", "Archiver.OperationUi", "Archiver.CLI"];

    private static XDocument LoadProject(string name) =>
        XDocument.Load(Path.Combine(RepoRoot, "src", name, name + ".csproj"));

    private static List<string> Property(XDocument project, string name) =>
        [.. project.Descendants().Where(e => e.Name.LocalName == name).Select(e => e.Value.Trim())];

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "windows-archiver-wrapper.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }
}
