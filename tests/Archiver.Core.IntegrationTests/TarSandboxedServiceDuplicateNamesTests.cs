using System.Text;
using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// T-F171: entries sharing one name inside a tar-family archive, and sources sharing one name when
/// creating one. tar.exe writes every copy to the same quarantine path, so only the last survives
/// its own extraction; a second sandboxed pass (-q, first match) recovers the first copy, and both
/// then go through the conflict rule like ZIP's duplicate entries (T-F30).
/// </summary>
[Collection("TarSandbox")]
public sealed class TarSandboxedServiceDuplicateNamesTests : IDisposable
{
    private readonly TarSandboxedService _sut = new(new GroupPolicyOptions());
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string WriteArchive(params (string Name, string Content)[] entries)
    {
        string archivePath = Path.Combine(_temp.Path, "dup-" + Path.GetRandomFileName() + ".tar");
        TarBuilder.WriteTar(archivePath,
            entries.Select(e => new TarBuilder.Entry { Name = e.Name, Content = Encoding.ASCII.GetBytes(e.Content) }));
        return archivePath;
    }

    private async Task<(ArchiveResult Result, string DestDir)> ExtractAsync(
        string archivePath, ConflictBehavior onConflict,
        IReadOnlyList<string>? selected = null, Func<ConflictInfo, Task<ConflictDecision>>? ask = null)
    {
        string destDir = Path.Combine(_temp.Path, "out-" + Path.GetRandomFileName());
        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archivePath],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
            OnConflict = onConflict,
            SelectedEntryPaths = selected,
            ResolveConflictAsync = ask,
        });
        return (result, destDir);
    }

    private static Dictionary<string, string> ReadTree(string root) =>
        Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(root, f).Replace('\\', '/'), File.ReadAllText);

    [Integration]
    public async Task ExtractAsync_DuplicateNames_Rename_KeepsFirstUnderNameAndLastRenamed()
    {
        string archive = WriteArchive(("dup.txt", "first"), ("dup.txt", "second"));

        (ArchiveResult result, string dest) = await ExtractAsync(archive, ConflictBehavior.Rename);

        result.Errors.Should().BeEmpty();
        ReadTree(dest).Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["dup.txt"] = "first",
            ["dup (1).txt"] = "second",
        });
    }

    [Integration]
    public async Task ExtractAsync_DuplicateNames_Skip_KeepsFirst()
    {
        string archive = WriteArchive(("dup.txt", "first"), ("dup.txt", "second"));

        (ArchiveResult result, string dest) = await ExtractAsync(archive, ConflictBehavior.Skip);

        result.Errors.Should().BeEmpty();
        ReadTree(dest).Should().BeEquivalentTo(new Dictionary<string, string> { ["dup.txt"] = "first" });
    }

    [Integration]
    public async Task ExtractAsync_DuplicateNames_Overwrite_KeepsLast()
    {
        string archive = WriteArchive(("dup.txt", "first"), ("dup.txt", "second"));

        (ArchiveResult result, string dest) = await ExtractAsync(archive, ConflictBehavior.Overwrite);

        result.Errors.Should().BeEmpty();
        ReadTree(dest).Should().BeEquivalentTo(new Dictionary<string, string> { ["dup.txt"] = "second" });
    }

    [Integration]
    public async Task ExtractAsync_DuplicateNames_Ask_PromptsOnceForTheSecondCopy()
    {
        string archive = WriteArchive(("dup.txt", "first"), ("dup.txt", "second"));
        int prompts = 0;

        (ArchiveResult result, string dest) = await ExtractAsync(archive, ConflictBehavior.Ask, ask: _ =>
        {
            prompts++;
            return Task.FromResult(new ConflictDecision { Resolution = ConflictResolution.Rename });
        });

        result.Errors.Should().BeEmpty();
        prompts.Should().Be(1);
        ReadTree(dest).Should().HaveCount(2);
    }

    [Integration]
    public async Task ExtractAsync_ThreeCopies_ExtractsFirstAndLastAndReportsTheMiddleOne()
    {
        string archive = WriteArchive(("dup.txt", "first"), ("dup.txt", "middle"), ("dup.txt", "last"));

        (ArchiveResult result, string dest) = await ExtractAsync(archive, ConflictBehavior.Rename);

        result.Errors.Should().BeEmpty();
        ReadTree(dest).Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["dup.txt"] = "first",
            ["dup (1).txt"] = "last",
        });
        result.SkippedFiles.Should().ContainSingle(s => s.Text != null && s.Text.Code == MessageCode.TarDuplicateCopiesNotExtracted);
    }

    [Integration]
    public async Task ExtractAsync_NamesDifferingOnlyInCase_BothExtracted()
    {
        string archive = WriteArchive(("A.txt", "one"), ("a.txt", "two"));

        (ArchiveResult result, string dest) = await ExtractAsync(archive, ConflictBehavior.Rename);

        result.Errors.Should().BeEmpty();
        ReadTree(dest).Values.Should().BeEquivalentTo(["one", "two"]);
    }

    [Integration]
    public async Task ExtractAsync_SelectedDuplicateName_ExtractsBothCopiesOnly()
    {
        string archive = WriteArchive(("d/f.txt", "first"), ("d/g.txt", "other"), ("d/f.txt", "second"));

        (ArchiveResult result, string dest) = await ExtractAsync(archive, ConflictBehavior.Rename, selected: ["d/f.txt"]);

        result.Errors.Should().BeEmpty();
        ReadTree(dest).Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["d/f.txt"] = "first",
            ["d/f (1).txt"] = "second",
        });
    }

    // T-F284: a member is a pattern, so "a[1].txt" in the first-copy pass matches "a1.txt". Only a
    // file at the exact expected path with the first copy's size is taken; otherwise the last copy
    // alone is kept and reported, never another entry's content under the duplicate's name.
    [Integration]
    public async Task ExtractAsync_DuplicateNameThatIsAlsoAPattern_NeverTakesAnotherEntrysContent()
    {
        string archive = WriteArchive(("a1.txt", "decoy"), ("a[1].txt", "first"), ("a[1].txt", "second"));

        (ArchiveResult result, string dest) = await ExtractAsync(archive, ConflictBehavior.Rename);

        result.Errors.Should().BeEmpty();
        Dictionary<string, string> tree = ReadTree(dest);
        tree["a1.txt"].Should().Be("decoy");
        tree.Where(kv => kv.Key != "a1.txt").Select(kv => kv.Value).Should().NotContain("decoy");
        tree["a[1].txt"].Should().Be("second");
        result.SkippedFiles.Should().ContainSingle(s => s.Text != null && s.Text.Code == MessageCode.TarDuplicateCopiesNotExtracted);
    }

    [Integration]
    public async Task ExtractAsync_NoDuplicates_NothingReported()
    {
        string archive = WriteArchive(("a.txt", "a"), ("b.txt", "b"));

        (ArchiveResult result, string dest) = await ExtractAsync(archive, ConflictBehavior.Rename);

        result.Errors.Should().BeEmpty();
        result.SkippedFiles.Should().BeEmpty();
        ReadTree(dest).Should().HaveCount(2);
    }

    private async Task<(ArchiveResult Result, Dictionary<string, string> Tree)> CompressAndExtractAsync(params string[] sources)
    {
        string archiveName = "c-" + Path.GetRandomFileName();
        ArchiveResult result = await _sut.CompressAsync(new ArchiveOptions
        {
            SourcePaths = sources,
            DestinationFolder = _temp.Path,
            ArchiveName = archiveName,
            Format = ArchiveContainerFormat.Tar,
        });
        string destDir = Path.Combine(_temp.Path, "x-" + Path.GetRandomFileName());
        ArchiveResult extract = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [Path.Combine(_temp.Path, archiveName + ".tar")],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
        });
        extract.Errors.Should().BeEmpty();
        return (result, ReadTree(destDir));
    }

    private static string[] StagingFolders() =>
        Directory.GetDirectories(Path.GetTempPath(), "PakkoTarStage_*");

    [Integration]
    public async Task CompressAsync_TwoFolderSourcesShareName_SecondRenamedAndSourcesIntact()
    {
        string a = Path.Combine(_temp.Path, "A", "x");
        string b = Path.Combine(_temp.Path, "B", "x");
        Directory.CreateDirectory(Path.Combine(b, "sub"));
        Directory.CreateDirectory(a);
        File.WriteAllText(Path.Combine(a, "f.txt"), "from A");
        File.WriteAllText(Path.Combine(b, "f.txt"), "from B");
        File.WriteAllText(Path.Combine(b, "sub", "g.txt"), "deep B");
        string[] stagingBefore = StagingFolders();

        (ArchiveResult result, Dictionary<string, string> tree) = await CompressAndExtractAsync(a, b);

        result.Errors.Should().BeEmpty();
        tree.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["x/f.txt"] = "from A",
            ["x (1)/f.txt"] = "from B",
            ["x (1)/sub/g.txt"] = "deep B",
        });
        File.ReadAllText(Path.Combine(b, "sub", "g.txt")).Should().Be("deep B");
        StagingFolders().Should().BeEquivalentTo(stagingBefore);
    }

    [Integration]
    public async Task CompressAsync_FileThenFolderShareName_FolderRenamed()
    {
        string file = Path.Combine(_temp.Path, "A", "x");
        string folder = Path.Combine(_temp.Path, "B", "x");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        Directory.CreateDirectory(folder);
        File.WriteAllText(file, "file x");
        File.WriteAllText(Path.Combine(folder, "f.txt"), "in folder");

        (ArchiveResult result, Dictionary<string, string> tree) = await CompressAndExtractAsync(file, folder);

        result.Errors.Should().BeEmpty();
        tree.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["x"] = "file x",
            ["x (1)/f.txt"] = "in folder",
        });
    }

    // A later collision that cannot be staged fails only its own source, and the junction staged
    // before it is still removed.
    [Integration]
    public async Task CompressAsync_LaterCollisionCannotBeStaged_ErrorForThatSourceAndNoStagingLeft()
    {
        string a = Path.Combine(_temp.Path, "A", "x");
        string b = Path.Combine(_temp.Path, "B", "x");
        string c = Path.Combine(_temp.Path, "C", "y.txt");
        string d = Path.Combine(_temp.Path, "D", "y.txt");
        Directory.CreateDirectory(a);
        Directory.CreateDirectory(b);
        Directory.CreateDirectory(Path.GetDirectoryName(c)!);
        Directory.CreateDirectory(Path.GetDirectoryName(d)!);
        File.WriteAllText(Path.Combine(b, "keep.txt"), "keep");
        File.WriteAllText(c, "c");
        File.WriteAllText(d, "d");
        string[] stagingBefore = StagingFolders();

        ArchiveResult result;
        using (new FileStream(d, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = await _sut.CompressAsync(new ArchiveOptions
            {
                SourcePaths = [a, b, c, d],
                DestinationFolder = _temp.Path,
                ArchiveName = "locked",
                Format = ArchiveContainerFormat.Tar,
            });
        }

        result.Errors.Should().ContainSingle(e => e.SourcePath == d);
        StagingFolders().Should().BeEquivalentTo(stagingBefore);
        File.ReadAllText(Path.Combine(b, "keep.txt")).Should().Be("keep");
    }

    // T-F283: a list line starting with '@' is a name, not "read entries from this archive".
    [Integration]
    public async Task CompressAsync_SourceNameStartingWithAt_ArchivedAsAFile()
    {
        string file = Path.Combine(_temp.Path, "src", "@x.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "at");

        (ArchiveResult result, Dictionary<string, string> tree) = await CompressAndExtractAsync(file);

        result.Errors.Should().BeEmpty();
        tree.Should().BeEquivalentTo(new Dictionary<string, string> { ["@x.txt"] = "at" });
    }
}
