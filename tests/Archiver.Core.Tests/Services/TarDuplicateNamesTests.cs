using System.Buffers.Binary;
using System.Text;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F171: the pure parts — which same-named entries get a first-copy pass, which folder sources
// can be staged as a junction, and the junction's reparse data.
public sealed class TarDuplicateNamesTests
{
    [Fact]
    public void FindDuplicateGroups_KeepsFirstNameAndSizeAndCountsCopies()
    {
        List<TarSandboxedService.DuplicateGroup> groups = TarSandboxedService.FindDuplicateGroups(
            [("a.txt", 5), ("b.txt", 1), ("a.txt", 6), ("a.txt", 7)], ["a.txt", "b.txt", "a.txt", "a.txt"], null);

        groups.Should().Equal(new TarSandboxedService.DuplicateGroup("a.txt", 5, 3));
    }

    [Fact]
    public void FindDuplicateGroups_NamesDifferingInCaseOrDotSlash_AreOneGroup()
    {
        List<TarSandboxedService.DuplicateGroup> groups = TarSandboxedService.FindDuplicateGroups(
            [("./A.txt", 1), ("a.txt", 2)], ["./A.txt", "a.txt"], null);

        groups.Should().Equal(new TarSandboxedService.DuplicateGroup("./A.txt", 1, 2));
    }

    [Fact]
    public void FindDuplicateGroups_OnlyAmongTheSelection()
    {
        List<TarSandboxedService.DuplicateGroup> groups = TarSandboxedService.FindDuplicateGroups(
            [("a.txt", 1), ("b.txt", 1), ("a.txt", 2), ("b.txt", 2)], ["a.txt", "b.txt", "a.txt", "b.txt"], ["b.txt"]);

        groups.Should().Equal(new TarSandboxedService.DuplicateGroup("b.txt", 1, 2));
    }

    [Fact]
    public void FindDuplicateGroups_NameThatIsAlsoAFolder_IsLeftOut()
    {
        List<TarSandboxedService.DuplicateGroup> groups = TarSandboxedService.FindDuplicateGroups(
            [("x", 1), ("x", 2)], ["x/", "x", "x"], null);

        groups.Should().BeEmpty();
    }

    [Fact]
    public void FindDuplicateGroups_NoDuplicates_Empty()
    {
        TarSandboxedService.FindDuplicateGroups([("a", 1), ("b", 1)], ["a", "b"], null).Should().BeEmpty();
    }

    [Theory]
    [InlineData(@"\\server\share\x", true)]
    [InlineData(@"\\?\UNC\server\share\x", true)]
    [InlineData(@"\\?\C:\x", false)]
    [InlineData(@"C:\x", false)]
    public void IsNetworkPath_ClassifiesSharesAndLocalPaths(string path, bool expected)
    {
        DirectoryJunction.IsNetworkPath(path).Should().Be(expected);
    }

    [Fact]
    public void BuildMountPointData_HasTheMountPointLayout()
    {
        byte[] data = DirectoryJunction.BuildMountPointData(@"C:\src\x\");
        byte[] substitute = Encoding.Unicode.GetBytes(@"\??\C:\src\x");
        byte[] print = Encoding.Unicode.GetBytes(@"C:\src\x");

        BinaryPrimitives.ReadUInt32LittleEndian(data).Should().Be(0xA0000003);
        BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(4)).Should().Be((ushort)(data.Length - 8));
        BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(8)).Should().Be(0);
        BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(10)).Should().Be((ushort)substitute.Length);
        BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(12)).Should().Be((ushort)(substitute.Length + 2));
        BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(14)).Should().Be((ushort)print.Length);
        data.AsSpan(16, substitute.Length).ToArray().Should().Equal(substitute);
        data.AsSpan(16 + substitute.Length + 2, print.Length).ToArray().Should().Equal(print);
        data.Length.Should().Be(16 + substitute.Length + 2 + print.Length + 2);
    }

    [Fact]
    public void Create_ThenNonRecursiveDelete_RemovesOnlyTheLink()
    {
        string root = Path.Combine(Path.GetTempPath(), "PakkoJunctionTest_" + Guid.NewGuid().ToString("N"));
        string target = Path.Combine(root, "target");
        string link = Path.Combine(root, "link");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "keep.txt"), "keep");
        try
        {
            DirectoryJunction.Create(link, target);
            File.ReadAllText(Path.Combine(link, "keep.txt")).Should().Be("keep");
            new DirectoryInfo(link).Attributes.Should().HaveFlag(FileAttributes.ReparsePoint);

            Directory.Delete(link, recursive: false);

            Directory.Exists(link).Should().BeFalse();
            File.ReadAllText(Path.Combine(target, "keep.txt")).Should().Be("keep");
        }
        finally
        {
            if (Directory.Exists(link))
                Directory.Delete(link, recursive: false);
            Directory.Delete(root, recursive: true);
        }
    }
}
