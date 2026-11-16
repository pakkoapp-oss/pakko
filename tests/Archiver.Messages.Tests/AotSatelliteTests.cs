using FluentAssertions;

namespace Archiver.Messages.Tests;

// T-F548: ILCompiler 11 compiles a ProjectReference's satellites into a Native AOT exe only through
// the PakkoAotProjectSatellites target (T-F492); an exe without it shows Core's messages in English
// whatever the user's language, and `dotnet test` (JIT) cannot see it.
public sealed class AotSatelliteTests
{
    [Fact]
    public void EveryAotExeThatRendersMessages_CompilesTheirSatellitesIn()
    {
        string[] projects = Directory.GetFiles(Path.Combine(LocalizedSources.RepoRoot, "src"), "*.csproj", SearchOption.AllDirectories);
        string[] renderers =
        [
            .. projects.Where(p =>
            {
                string text = File.ReadAllText(p);
                return text.Contains("<PublishAot>true</PublishAot>", StringComparison.Ordinal)
                    && text.Contains(@"Archiver.Messages\Archiver.Messages.csproj", StringComparison.Ordinal);
            }),
        ];

        renderers.Select(Path.GetFileName).Should().Contain(["Archiver.App.csproj", "Archiver.Shell.csproj"]);
        renderers.Where(p => !File.ReadAllText(p).Contains("Name=\"PakkoAotProjectSatellites\"", StringComparison.Ordinal))
            .Select(Path.GetFileName).Should().BeEmpty();
    }
}
