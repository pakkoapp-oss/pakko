using Archiver.Core.IO;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;
using Lister = System.Func<string, System.Collections.Generic.IReadOnlyDictionary<string, Archiver.Core.IO.PathIdentity.Identity>?>;

namespace Archiver.Core.Tests.Services;

/// <summary>T-F392: the pair search over folder listings, with the listing and the confirmation faked.</summary>
public sealed class TarHardLinkPairsTests
{
    private static readonly PathIdentity.Identity Shared = new(7, 42, 0);

    private static Lister Listing(
        Dictionary<string, Dictionary<string, PathIdentity.Identity>?> byFolder) =>
        folder => byFolder.TryGetValue(folder, out Dictionary<string, PathIdentity.Identity>? files) ? files : null;

    [Fact]
    public void Find_TwoNamesWithOneIdentityConfirmed_ReturnsThemInOrder()
    {
        Lister listing = Listing(new() { [@"C:\s"] = new() { ["a.txt"] = Shared, ["b.txt"] = Shared } });

        (string First, string Second)? pair = TarHardLinkPairs.Find([@"C:\s\a.txt", @"C:\s\b.txt"], listing, (_, _) => true);

        pair.Should().Be((@"C:\s\a.txt", @"C:\s\b.txt"));
    }

    [Fact]
    public void Find_PairAcrossTwoFolders_IsFound()
    {
        Lister listing = Listing(new()
        {
            [@"C:\s\x"] = new() { ["a.txt"] = Shared },
            [@"C:\s\y"] = new() { ["b.txt"] = Shared },
        });

        TarHardLinkPairs.Find([@"C:\s\x\a.txt", @"C:\s\y\b.txt"], listing, (_, _) => true).Should().NotBeNull();
    }

    [Fact]
    public void Find_DistinctIdentities_ReturnsNull()
    {
        Lister listing = Listing(new() { [@"C:\s"] = new() { ["a.txt"] = new(7, 1, 0), ["b.txt"] = new(7, 2, 0) } });

        TarHardLinkPairs.Find([@"C:\s\a.txt", @"C:\s\b.txt"], listing, (_, _) => true).Should().BeNull();
    }

    [Fact]
    public void Find_SameIdOnTwoVolumes_IsNotAPair()
    {
        Lister listing = Listing(new()
        {
            [@"C:\s"] = new() { ["a.txt"] = new(1, 42, 0) },
            [@"D:\s"] = new() { ["a.txt"] = new(2, 42, 0) },
        });

        TarHardLinkPairs.Find([@"C:\s\a.txt", @"D:\s\a.txt"], listing, (_, _) => true).Should().BeNull();
    }

    // --- Boundary: a listing that tells nothing refuses nothing ---

    [Fact]
    public void Find_ZeroIdentities_AreNotCompared()
    {
        PathIdentity.Identity zero = new(7, 0, 0);
        Lister listing = Listing(new() { [@"C:\s"] = new() { ["a.txt"] = zero, ["b.txt"] = zero } });

        TarHardLinkPairs.Find([@"C:\s\a.txt", @"C:\s\b.txt"], listing, (_, _) => true).Should().BeNull();
    }

    [Fact]
    public void Find_FolderThatCannotBeListed_RefusesNothing()
    {
        Lister listing = Listing(new() { [@"C:\s"] = null });

        TarHardLinkPairs.Find([@"C:\s\a.txt", @"C:\s\b.txt"], listing, (_, _) => true).Should().BeNull();
    }

    [Fact]
    public void Find_NameMissingFromTheListing_IsSkipped()
    {
        Lister listing = Listing(new() { [@"C:\s"] = new() { ["a.txt"] = Shared } });

        TarHardLinkPairs.Find([@"C:\s\a.txt", @"C:\s\gone.txt"], listing, (_, _) => true).Should().BeNull();
    }

    // --- Misuse: a listing that claims a pair the files themselves deny ---

    [Fact]
    public void Find_DuplicateNotConfirmedByOpeningBoth_IsNotAPair()
    {
        Lister listing = Listing(new() { [@"C:\s"] = new() { ["a.txt"] = Shared, ["b.txt"] = Shared } });

        TarHardLinkPairs.Find([@"C:\s\a.txt", @"C:\s\b.txt"], listing, (_, _) => false).Should().BeNull();
    }

    [Fact]
    public void Find_EachFolderIsListedOnce()
    {
        int listed = 0;
        Dictionary<string, PathIdentity.Identity> files = new() { ["a.txt"] = new(7, 1, 0), ["b.txt"] = new(7, 2, 0), ["c.txt"] = new(7, 3, 0) };

        TarHardLinkPairs.Find([@"C:\s\a.txt", @"C:\s\b.txt", @"C:\s\c.txt"], _ => { listed++; return files; }, (_, _) => true);

        listed.Should().Be(1);
    }

    // --- Real volume: the listing of this machine's temp folder reads identities ---

    [Fact]
    public void FilesIn_RealFolder_ListsFilesNotFolders()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "a.txt"), "a");
        Directory.CreateDirectory(Path.Combine(temp.Path, "sub"));

        Dictionary<string, PathIdentity.Identity>? files = PathIdentity.FilesIn(temp.Path);

        files.Should().NotBeNull();
        files!.Keys.Should().BeEquivalentTo("a.txt");
        files["a.txt"].Should().Be(PathIdentity.Resolve(Path.Combine(temp.Path, "a.txt"))!.Id);
    }

    [Fact]
    public void FilesIn_MissingFolder_ReturnsNull() =>
        PathIdentity.FilesIn(Path.Combine(Path.GetTempPath(), "pakko-missing-" + Guid.NewGuid().ToString("N"))).Should().BeNull();
}
