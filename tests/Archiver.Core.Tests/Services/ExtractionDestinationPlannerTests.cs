using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

public sealed class ExtractionDestinationPlannerTests
{
    private const string DestDir = @"C:\Dest\archive";
    private const string UnisolatedDestDir = @"C:\Dest";

    // T-F157: one Fact per (alreadyIsolated, RootShape) combination, reproducing exactly the
    // pre-refactor behavior — ActualDest == unwrapSingleFile ? unisolatedDestDir : destDir
    // (T-F154), StripRootPrefix == (shape == RootShape.SingleFolder) (pre-existing
    // isSingleRootFolder strip). Not a [Theory]/[InlineData] over RootShape directly — RootShape
    // is internal, and CS0051 forbids an internal type as a parameter on xUnit's required-public
    // test method (same rule as this project's CLAUDE.md "private nested class as parameter"
    // constraint, generalized to internal enums).
    [Fact]
    public void Resolve_AlreadyIsolated_SingleFile_BypassesToUnisolatedDest()
    {
        AssertResolve(alreadyIsolated: true, RootShape.SingleFile, expectedDest: UnisolatedDestDir, expectedStrip: false);
    }

    [Fact]
    public void Resolve_AlreadyIsolated_SingleFolder_UsesDestDirAndStrips()
    {
        AssertResolve(alreadyIsolated: true, RootShape.SingleFolder, expectedDest: DestDir, expectedStrip: true);
    }

    [Fact]
    public void Resolve_AlreadyIsolated_MultiRoot_UsesDestDirNoStrip()
    {
        AssertResolve(alreadyIsolated: true, RootShape.MultiRoot, expectedDest: DestDir, expectedStrip: false);
    }

    [Fact]
    public void Resolve_AlreadyIsolated_SelectedSubset_UsesDestDirNoStrip()
    {
        AssertResolve(alreadyIsolated: true, RootShape.SelectedSubset, expectedDest: DestDir, expectedStrip: false);
    }

    [Fact]
    public void Resolve_NotIsolated_SingleFile_UsesDestDirNoStrip()
    {
        AssertResolve(alreadyIsolated: false, RootShape.SingleFile, expectedDest: DestDir, expectedStrip: false);
    }

    [Fact]
    public void Resolve_NotIsolated_SingleFolder_UsesDestDirAndStrips()
    {
        AssertResolve(alreadyIsolated: false, RootShape.SingleFolder, expectedDest: DestDir, expectedStrip: true);
    }

    [Fact]
    public void Resolve_NotIsolated_MultiRoot_UsesDestDirNoStrip()
    {
        AssertResolve(alreadyIsolated: false, RootShape.MultiRoot, expectedDest: DestDir, expectedStrip: false);
    }

    [Fact]
    public void Resolve_NotIsolated_SelectedSubset_UsesDestDirNoStrip()
    {
        AssertResolve(alreadyIsolated: false, RootShape.SelectedSubset, expectedDest: DestDir, expectedStrip: false);
    }

    private static void AssertResolve(bool alreadyIsolated, RootShape shape, string expectedDest, bool expectedStrip)
    {
        var (actualDest, stripRootPrefix) = ExtractionDestinationPlanner.Resolve(
            alreadyIsolated, shape, DestDir, UnisolatedDestDir);

        actualDest.Should().Be(expectedDest);
        stripRootPrefix.Should().Be(expectedStrip);
    }

    // The actual "adding a new RootShape must be handled" guard — see ExtractionDestinationPlanner's
    // own comment on why this, not a discard-less switch, is the enforcement mechanism (T-F157).
    [Fact]
    public void Resolve_EveryRealRootShapeValue_NeverThrows()
    {
        foreach (RootShape shape in Enum.GetValues<RootShape>())
        {
            foreach (bool alreadyIsolated in new[] { true, false })
            {
                Action act = () => ExtractionDestinationPlanner.Resolve(alreadyIsolated, shape, DestDir, UnisolatedDestDir);
                act.Should().NotThrow($"RootShape.{shape} with alreadyIsolated={alreadyIsolated} must be an explicit arm");
            }
        }
    }

    [Fact]
    public void Classify_SelectedSubset_WinsOverFolderAndFile()
    {
        ExtractionDestinationPlanner.Classify(isSelectedSubset: true, isSingleRootFolder: true, isSingleRootFile: true)
            .Should().Be(RootShape.SelectedSubset);
    }

    [Fact]
    public void Classify_SingleRootFolder_WinsOverSingleRootFile()
    {
        ExtractionDestinationPlanner.Classify(isSelectedSubset: false, isSingleRootFolder: true, isSingleRootFile: true)
            .Should().Be(RootShape.SingleFolder);
    }

    [Fact]
    public void Classify_SingleRootFileOnly_ReturnsSingleFile()
    {
        ExtractionDestinationPlanner.Classify(isSelectedSubset: false, isSingleRootFolder: false, isSingleRootFile: true)
            .Should().Be(RootShape.SingleFile);
    }

    [Fact]
    public void Classify_NeitherFolderNorFile_ReturnsMultiRoot()
    {
        ExtractionDestinationPlanner.Classify(isSelectedSubset: false, isSingleRootFolder: false, isSingleRootFile: false)
            .Should().Be(RootShape.MultiRoot);
    }
}
