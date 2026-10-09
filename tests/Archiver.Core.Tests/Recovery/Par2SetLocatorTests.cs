using Archiver.Core.Recovery;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Recovery;

// T-F275: which PAR2 files form a set and which file the set protects.
public sealed class Par2SetLocatorTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Theory]
    [InlineData("a.zip.par2", "a.zip")]
    [InlineData("a.zip.vol0+5.par2", "a.zip")]
    [InlineData("a.zip.vol000+100.PAR2", "a.zip")]
    [InlineData("a.vol01-03.par2", "a")]
    [InlineData("a.par2", "a")]
    [InlineData("a.zip.volx+1.par2", "a.zip.volx+1")]
    [InlineData("a.vol1+2.vol3+4.par2", "a.vol1+2")]
    public void BaseName_StripsExtensionAndVolumeSuffix(string name, string expected)
    {
        Par2SetLocator.BaseName(name).Should().Be(expected);
    }

    [Theory]
    [InlineData("a.zip")]
    [InlineData(".par2")]
    [InlineData("a.par2.bak")]
    public void BaseName_NotAPar2Name_IsNull(string name)
    {
        Par2SetLocator.BaseName(name).Should().BeNull();
    }

    [Fact]
    public void SetFiles_TakesTheBaseAndItsVolumesOnly()
    {
        foreach (string name in new[] { "a.zip.par2", "a.zip.vol0+5.par2", "A.ZIP.vol5+5.par2", "b.zip.par2", "a.zipper.par2", "a.zip" })
            _temp.CreateFile(name);
        Directory.CreateDirectory(Path.Combine(_temp.Path, "a.zip.dir.par2"));

        Par2SetLocator.SetFiles(_temp.Path, "a.zip").Select(Path.GetFileName)
            .Should().BeEquivalentTo("a.zip.par2", "a.zip.vol0+5.par2", "A.ZIP.vol5+5.par2");
    }

    [Fact]
    public void SetFilesForPar2_FromAVolume_FindsTheWholeSet()
    {
        _temp.CreateFile("a.zip.par2");
        string volume = _temp.CreateFile("a.zip.vol0+5.par2");

        Par2SetLocator.SetFilesForPar2(volume).Select(Path.GetFileName).Should().Equal("a.zip.par2", "a.zip.vol0+5.par2");
    }

    [Fact]
    public void SetFilesForTarget_PrefersTheFullName()
    {
        _temp.CreateFile("a.zip.par2");
        _temp.CreateFile("a.par2");

        Par2SetLocator.SetFilesForTarget(Path.Combine(_temp.Path, "a.zip")).Select(Path.GetFileName).Should().Equal("a.zip.par2");
    }

    [Fact]
    public void SetFilesForTarget_FallsBackToTheNameWithoutExtension()
    {
        _temp.CreateFile("a.par2");
        _temp.CreateFile("a.vol0+1.par2");

        Par2SetLocator.SetFilesForTarget(Path.Combine(_temp.Path, "a.zip")).Select(Path.GetFileName).Should().Equal("a.par2", "a.vol0+1.par2");
    }

    [Fact]
    public void SetFilesForTarget_TarGz_TriesTheNameWithoutTheLastExtension()
    {
        _temp.CreateFile("a.tar.par2");

        Par2SetLocator.SetFilesForTarget(Path.Combine(_temp.Path, "a.tar.gz")).Select(Path.GetFileName).Should().Equal("a.tar.par2");
    }

    [Fact]
    public void SetFilesForTarget_NoSet_IsEmpty()
    {
        Par2SetLocator.SetFilesForTarget(Path.Combine(_temp.Path, "a.zip")).Should().BeEmpty();
    }

    [Fact]
    public void TargetCandidates_BaseFirstThenSiblingsThatAreNotPar2()
    {
        foreach (string name in new[] { "a.par2", "a.vol0+1.par2", "a.zip", "a.7z", "ab.zip" })
            _temp.CreateFile(name);

        Par2SetLocator.TargetCandidates(Path.Combine(_temp.Path, "a.vol0+1.par2")).Select(Path.GetFileName)
            .Should().Equal("a", "a.7z", "a.zip");
    }

    [Fact]
    public void Select_ByName()
    {
        string file = Par2TestData.WriteContent(_temp.Path, "a.zip", 1000);
        Par2Set set = CreateAndRead(file);

        Par2SetLocator.Select([set], file).Should().Be(new Par2Match(set, NameMatches: true));
    }

    [Fact]
    public void Select_RenamedArchive_MatchesByContentWithAWarning()
    {
        string file = Par2TestData.WriteContent(_temp.Path, "a.zip", 1000);
        Par2Set set = CreateAndRead(file);
        string renamed = Path.Combine(_temp.Path, "renamed.zip");
        File.Move(file, renamed);

        Par2SetLocator.Select([set], renamed).Should().Be(new Par2Match(set, NameMatches: false));
    }

    [Fact]
    public void Select_RenamedAndDamagedAtTheStart_FindsNothing()
    {
        string file = Par2TestData.WriteContent(_temp.Path, "a.zip", 1000);
        Par2Set set = CreateAndRead(file);
        string renamed = Path.Combine(_temp.Path, "renamed.zip");
        File.Move(file, renamed);
        using (FileStream stream = File.OpenWrite(renamed))
            stream.WriteByte(0xFF);

        Par2SetLocator.Select([set], renamed).Should().BeNull();
    }

    [Fact]
    public void Select_MissingFile_MatchesByName()
    {
        string file = Par2TestData.WriteContent(_temp.Path, "a.zip", 1000);
        Par2Set set = CreateAndRead(file);
        File.Delete(file);

        Par2SetLocator.Select([set], file).Should().Be(new Par2Match(set, NameMatches: true));
    }

    [Fact]
    public void Select_SetOfAnotherFile_FindsNothing()
    {
        string other = Par2TestData.WriteContent(_temp.Path, "other.zip", 1000);
        Par2Set set = CreateAndRead(other);
        string file = Path.Combine(_temp.Path, "a.zip");
        File.WriteAllBytes(file, new byte[1000]);

        Par2SetLocator.Select([set], file).Should().BeNull();
    }

    [Fact]
    public void Select_NameAndContentBeatsNameOnly()
    {
        string file = Par2TestData.WriteContent(_temp.Path, "a.zip", 1000);
        Par2Set current = CreateAndRead(file);
        File.WriteAllBytes(file, new byte[2000]);
        Par2Set stale = CreateAndRead(file, percent: 50);
        Par2TestData.WriteContent(_temp.Path, "a.zip", 1000);

        Par2SetLocator.Select([stale, current], file)!.Set.Should().BeSameAs(current);
    }

    [Fact]
    public void Select_TwoSetsForTheSameFile_TakesTheOneWithMoreBlocks()
    {
        string file = Par2TestData.WriteContent(_temp.Path, "a.zip", 1000);
        Par2Set small = CreateAndRead(file, percent: 5);
        Par2Set large = CreateAndRead(file, percent: 40);

        Par2SetLocator.Select([small, large], file)!.Set.Should().BeSameAs(large);
    }

    [Fact]
    public void Select_HostileNameInTheSet_IsNeverAPath()
    {
        var (_, _, packets) = Par2PacketForge.OneFileSet(8, 16, @"..\..\a.zip");
        Par2Set set = Par2PacketReader.Read([Par2PacketForge.Write(_temp.Path, "x.par2", packets)], CancellationToken.None).Sets.Single();

        Par2SetLocator.Select([set], Path.Combine(_temp.Path, "a.zip")).Should().BeNull();
    }

    private Par2Set CreateAndRead(string file, int percent = 5)
    {
        string folder = Path.Combine(_temp.Path, "sets", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string copy = Path.Combine(folder, Path.GetFileName(file));
        File.Copy(file, copy);
        Par2CreateResult created = Par2Creator.Create(copy, Par2Creator.ChooseParameters(new FileInfo(copy).Length, percent)!.Value, null, CancellationToken.None);
        return Par2PacketReader.Read([created.IndexPath, created.VolumePath], CancellationToken.None).Sets.Single();
    }
}
