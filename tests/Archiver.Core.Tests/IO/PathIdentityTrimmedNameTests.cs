using Archiver.Core.IO;
using FluentAssertions;

namespace Archiver.Core.Tests.IO;

// T-F510: by a plain path, Windows opens "a.txt " as its twin "a.txt" and "d." as "d", so
// PathIdentity answered for another file: its identity and final path. It answers for nothing
// there now (unknown, which every caller already treats as the careful case); through \\?\, where
// Windows trims nothing, it answers for the name itself.
public sealed class PathIdentityTrimmedNameTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("pakko-identity-").FullName;
    private readonly string _file;
    private readonly string _folder;

    public PathIdentityTrimmedNameTests()
    {
        File.WriteAllText(Path.Combine(_root, "a.txt"), "twin");
        Directory.CreateDirectory(Path.Combine(_root, "d"));
        _file = Path.Combine(_root, "a.txt ");
        _folder = Path.Combine(_root, "d.");
        File.WriteAllText(@"\\?\" + _file, "itself");
        Directory.CreateDirectory(@"\\?\" + _folder);
        File.WriteAllText(@"\\?\" + Path.Combine(_folder, "inside.txt"), "inside d.");
    }

    public void Dispose()
    {
        File.Delete(@"\\?\" + Path.Combine(_folder, "inside.txt"));
        Directory.Delete(@"\\?\" + _folder);
        File.Delete(@"\\?\" + _file);
        Directory.Delete(_root, recursive: true);
    }

    public static TheoryData<string> Trimmed => ["a.txt ", "d.", @"d.\", @"d.\inside.txt"];

    [Theory]
    [MemberData(nameof(Trimmed))]
    public void PlainPath_AnswersForNothing(string relative)
    {
        string path = Path.Combine(_root, relative);
        PathIdentity.Identity twin = PathIdentity.Resolve(Path.Combine(_root, relative.Contains('d') ? "d" : "a.txt"))!.Id;

        PathIdentity.Resolve(path).Should().BeNull();
        PathIdentity.FinalPathOfItself(path).Should().BeNull();
        PathIdentity.SelfAndAncestors(path).Should().BeNull();
        PathIdentity.LeadsTo(path, twin).Should().BeFalse();
        PathIdentity.VolumeOf(path).Should().BeNull();
        PathIdentity.FilesIn(path).Should().BeNull();
    }

    [Fact]
    public void LongPathPrefix_AnswersForTheNameItself()
    {
        PathIdentity.Resolved file = PathIdentity.Resolve(@"\\?\" + _file)!;
        PathIdentity.Resolved folder = PathIdentity.Resolve(@"\\?\" + _folder)!;

        file.FinalPath.Should().EndWith("a.txt ");
        file.Id.Should().NotBe(PathIdentity.Resolve(Path.Combine(_root, "a.txt"))!.Id);
        folder.FinalPath.Should().EndWith("d.");
        folder.Id.Should().NotBe(PathIdentity.Resolve(Path.Combine(_root, "d"))!.Id);
        PathIdentity.LeadsTo(@"\\?\" + _file, file.Id).Should().BeTrue();
        PathIdentity.FilesIn(@"\\?\" + _folder).Should().ContainKey("inside.txt");
    }

    [Fact]
    public void OrdinaryNames_StillAnswer()
    {
        PathIdentity.Resolve(Path.Combine(_root, "a.txt")).Should().NotBeNull();
        PathIdentity.FinalPathOfItself(Path.Combine(_root, "d")).Should().NotBeNull();
        PathIdentity.SelfAndAncestors(Path.Combine(_root, "a.txt")).Should().NotBeNull();
    }
}
