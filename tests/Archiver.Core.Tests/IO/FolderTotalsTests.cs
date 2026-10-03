using Archiver.Core.IO;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.IO;

/// <summary>
/// T-F236: the App's pending list sized a folder with its own recursive walk that followed links,
/// could not be stopped and failed whole on one unreadable subfolder. It now uses the engines' walk.
/// </summary>
public sealed class FolderTotalsTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Tree()
    {
        string root = Path.Combine(_temp.Path, "root");
        Directory.CreateDirectory(Path.Combine(root, "a", "b"));
        File.WriteAllBytes(Path.Combine(root, "one.bin"), new byte[10]);
        File.WriteAllBytes(Path.Combine(root, "a", "two.bin"), new byte[20]);
        File.WriteAllBytes(Path.Combine(root, "a", "b", "three.bin"), new byte[30]);
        return root;
    }

    [Fact]
    public void Measure_CountsEveryFileAndByte()
    {
        FolderTotals.Measure(Tree()).Should().Be(new FolderTotals(60, 3));
    }

    [Fact]
    public void Measure_LinkToAnotherFolder_NotFollowed()
    {
        string root = Tree();
        string outside = Path.Combine(_temp.Path, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllBytes(Path.Combine(outside, "big.bin"), new byte[1000]);
        DirectoryJunction.Create(Path.Combine(root, "link"), outside);

        var totals = FolderTotals.Measure(root);

        Directory.Delete(Path.Combine(root, "link"), recursive: false);
        totals.Should().Be(new FolderTotals(60, 3));
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void Measure_UnreadableSubfolder_CountsTheRest()
    {
        string root = Tree();
        FolderTotals totals;
        using (new DeniedFolder(Path.Combine(root, "a", "b")))
            totals = FolderTotals.Measure(root);

        totals.Should().Be(new FolderTotals(30, 2));
    }

    [Fact]
    public void Measure_Cancelled_Throws()
    {
        Action act = () => FolderTotals.Measure(Tree(), new CancellationToken(canceled: true));

        act.Should().Throw<OperationCanceledException>();
    }
}
