using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F410: which list a filtered tar test hands tar.exe. libarchive matches every entry against
// every pattern, so the shorter side goes: "-T" with the files that passed or "-X" with the rest.
public sealed class TarTestSubsetTests
{
    private static readonly string[] Names = ["./", "./a.txt", "./dir/", "./dir/b.txt", "./c.txt", "./d.txt"];
    private static readonly Dictionary<string, long> Sizes = new()
    {
        ["./a.txt"] = 1, ["./dir/b.txt"] = 10, ["./c.txt"] = 100, ["./d.txt"] = 1000,
    };

    [Fact]
    public void Of_FewerPass_ListsThePassedFilesForMinusT()
    {
        TarSandboxedService.TestSubset subset = TarSandboxedService.TestSubset.Of(Names, Sizes, (name, _) => name == "c.txt");

        subset.Members.Should().Equal("./c.txt");
        subset.ExcludedFiles.Should().BeNull();
        subset.DeclaredSize.Should().Be(100);
        subset.NothingToRead.Should().BeFalse();
    }

    [Fact]
    public void Of_FewerLeftOut_ListsTheLeftOutFilesForMinusX()
    {
        TarSandboxedService.TestSubset subset = TarSandboxedService.TestSubset.Of(Names, Sizes, (name, _) => name != "c.txt");

        subset.Members.Should().BeNull();
        subset.ExcludedFiles.Should().Equal("./c.txt");
        subset.DeclaredSize.Should().Be(1011);
    }

    [Fact]
    public void Of_EveryFilePasses_NoList()
    {
        TarSandboxedService.TestSubset subset = TarSandboxedService.TestSubset.Of(Names, Sizes, (_, isFolder) => !isFolder);

        subset.Members.Should().BeNull();
        subset.ExcludedFiles.Should().BeNull();
        subset.DeclaredSize.Should().Be(1111);
    }

    [Fact]
    public void Of_OnlyFoldersPass_NothingToRead()
    {
        TarSandboxedService.TestSubset subset = TarSandboxedService.TestSubset.Of(Names, Sizes, (_, isFolder) => isFolder);

        subset.NothingToRead.Should().BeTrue();
    }

    [Fact]
    public void Of_IsNotAskedForTheArchiveRoot()
    {
        var asked = new List<string>();

        TarSandboxedService.TestSubset.Of(Names, Sizes, (name, _) =>
        {
            asked.Add(name);
            return true;
        });

        asked.Should().Equal("a.txt", "dir/", "dir/b.txt", "c.txt", "d.txt");
    }
}
