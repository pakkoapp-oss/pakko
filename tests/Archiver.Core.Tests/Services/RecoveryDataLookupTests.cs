using Archiver.Core.Models;
using Archiver.Core.Recovery;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using Archiver.Core.Tests.Recovery;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F275 step 3c: what a frontend asks before any test runs - are there PAR2 files next to this
// archive, and which archive does this .par2 protect.
public sealed class RecoveryDataLookupTests : IDisposable
{
    private const int Length = 10_000;
    private static readonly GroupPolicyOptions NoPolicy = new();
    private static readonly GroupPolicyOptions Disabled = new() { DisableRecoveryData = true };

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Archive(string name, int seedOffset = 0)
    {
        string path = Path.Combine(_temp.Path, name);
        byte[] content = Par2TestData.Content(Length + seedOffset);
        File.WriteAllBytes(path, content);
        return path;
    }

    private static Par2CreateResult Protect(string path) =>
        Par2Creator.Create(path, Par2Creator.ChooseParameters(new FileInfo(path).Length, 5)!.Value, null, CancellationToken.None);

    // --- HasFilesFor: happy ---

    [Theory]
    [InlineData("a.zip")]
    [InlineData("a.tar.gz")]
    public void HasFilesFor_ArchiveWithItsSet_True(string name)
    {
        string archive = Archive(name);
        Protect(archive);

        RecoveryDataLookup.HasFilesFor(archive, NoPolicy).Should().BeTrue();
    }

    [Fact]
    public void HasFilesFor_SetUnderTheShortName_True()
    {
        string archive = Archive("photos.zip");
        File.WriteAllBytes(Path.Combine(_temp.Path, "photos.par2"), [1, 2, 3]);

        RecoveryDataLookup.HasFilesFor(archive, NoPolicy).Should().BeTrue();
    }

    // --- HasFilesFor: the set is half there, or not this archive's ---

    [Fact]
    public void HasFilesFor_VolumeWithoutItsIndex_True()
    {
        string archive = Archive("a.tar.gz");
        File.Delete(Protect(archive).IndexPath);

        RecoveryDataLookup.HasFilesFor(archive, NoPolicy).Should().BeTrue();
    }

    [Fact]
    public void HasFilesFor_OnlyTemporaryFilesOrNamesThatStartAlike_False()
    {
        string archive = Archive("photos.zip");
        foreach (string name in new[] { ".pakko-a-12.tmp", "photos.zip.par2.tmp", "photos2.zip.par2", "photos-old.par2", "photosXpar2" })
            File.WriteAllBytes(Path.Combine(_temp.Path, name), [1]);

        RecoveryDataLookup.HasFilesFor(archive, NoPolicy).Should().BeFalse();
    }

    [Fact]
    public void HasFilesFor_NoSet_False() =>
        RecoveryDataLookup.HasFilesFor(Archive("a.zip"), NoPolicy).Should().BeFalse();

    // --- HasFilesFor: policy and misuse ---

    [Fact]
    public void HasFilesFor_UnderThePolicy_FalseWithoutLooking()
    {
        string archive = Archive("a.zip");
        Protect(archive);

        RecoveryDataLookup.HasFilesFor(archive, Disabled).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("\0")]
    [InlineData("C:\\")]
    [InlineData("Z:\\no\\such\\folder\\a.zip")]
    [InlineData("a|b<c>.zip")]
    public void HasFilesFor_UnusablePath_FalseNeverThrows(string path) =>
        RecoveryDataLookup.HasFilesFor(path, NoPolicy).Should().BeFalse();

    // --- FindArchive: happy ---

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FindArchive_IndexOrVolume_NamesTheArchive(bool index)
    {
        string archive = Archive("a.tar.gz");
        Par2CreateResult set = Protect(archive);

        RecoveryTarget target = RecoveryDataLookup.FindArchive(index ? set.IndexPath : set.VolumePath, NoPolicy);

        target.Error.Should().BeNull();
        target.ArchivePath.Should().Be(archive);
    }

    // The set says which file it protects by content: a name guess would take the first candidate.
    [Fact]
    public void FindArchive_TwoFilesShareTheBase_TheOneTheSetWasMadeFor()
    {
        string zip = Archive("photos.zip");
        string tar = Archive("photos.tar", seedOffset: 7);
        Par2CreateResult set = Protect(tar);
        string index = Path.Combine(_temp.Path, "photos.par2");
        File.Move(set.IndexPath, index);
        File.Delete(set.VolumePath);

        RecoveryTarget target = RecoveryDataLookup.FindArchive(index, NoPolicy);

        target.ArchivePath.Should().Be(tar);
        File.Exists(zip).Should().BeTrue();
    }

    // --- FindArchive: failures are always an error to show, never "nothing" ---

    [Fact]
    public void FindArchive_UnreadableSet_IsAnError()
    {
        Archive("a.zip");
        string par2 = Path.Combine(_temp.Path, "a.zip.par2");
        File.WriteAllBytes(par2, Par2TestData.Content(3_000));

        RecoveryTarget target = RecoveryDataLookup.FindArchive(par2, NoPolicy);

        target.ArchivePath.Should().BeNull();
        target.Error!.Text!.Code.Should().Be(MessageCode.RecoveryDataUnusable);
    }

    // A set that names its file still points at it when the file is gone: the test then says the
    // archive is missing, and a repair (step 4) can rebuild it whole.
    [Fact]
    public void FindArchive_ArchiveGone_StillNamesIt()
    {
        string archive = Archive("a.zip");
        Par2CreateResult set = Protect(archive);
        File.Delete(archive);

        RecoveryTarget target = RecoveryDataLookup.FindArchive(set.IndexPath, NoPolicy);

        target.Error.Should().BeNull();
        target.ArchivePath.Should().Be(archive);
    }

    [Fact]
    public void FindArchive_SetOfAnotherFileUnderThisName_IsAnError()
    {
        string other = Archive("other.zip", seedOffset: 3);
        Par2CreateResult set = Protect(other);
        Archive("a.zip");
        string par2 = Path.Combine(_temp.Path, "a.zip.par2");
        File.Move(set.IndexPath, par2);
        File.Delete(set.VolumePath);
        File.Delete(other);

        RecoveryTarget target = RecoveryDataLookup.FindArchive(par2, NoPolicy);

        target.ArchivePath.Should().BeNull();
        target.Error!.Text!.Code.Should().Be(MessageCode.RecoveryDataTargetNotFound);
    }

    [Fact]
    public void FindArchive_UnderThePolicy_IsRefused()
    {
        string archive = Archive("a.zip");
        Par2CreateResult set = Protect(archive);

        RecoveryTarget target = RecoveryDataLookup.FindArchive(set.IndexPath, Disabled);

        target.ArchivePath.Should().BeNull();
        target.Error!.Text!.Code.Should().Be(MessageCode.RecoveryDataDisabled);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\0.par2")]
    [InlineData("Z:\\no\\such\\folder\\a.zip.par2")]
    [InlineData("a|b<c>.par2")]
    [InlineData("C:\\Windows\\notepad.exe")]
    public void FindArchive_UnusablePath_IsAnErrorNeverAThrow(string path)
    {
        RecoveryTarget target = RecoveryDataLookup.FindArchive(path, NoPolicy);

        target.ArchivePath.Should().BeNull();
        target.Error.Should().NotBeNull();
    }

    [Fact]
    public void FindArchive_Cancelled_Throws()
    {
        string archive = Archive("a.zip");
        Par2CreateResult set = Protect(archive);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Action find = () => RecoveryDataLookup.FindArchive(set.IndexPath, NoPolicy, cts.Token);

        find.Should().Throw<OperationCanceledException>();
    }

    [Theory]
    [InlineData("a.zip.par2", true)]
    [InlineData("A.VOL0+1.PAR2", true)]
    [InlineData("a.zip", false)]
    [InlineData("a.par2.bak", false)]
    [InlineData("", false)]
    public void IsRecoveryFile_ByExtension(string path, bool expected) =>
        RecoveryDataLookup.IsRecoveryFile(path).Should().Be(expected);
}
