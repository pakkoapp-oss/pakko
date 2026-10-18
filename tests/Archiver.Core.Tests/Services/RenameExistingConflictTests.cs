using System.IO.Compression;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

/// <summary>
/// T-F411: <see cref="ConflictBehavior.RenameExisting"/> (7-Zip's -aot). Measured on 7za 26.02: the
/// file already there is renamed and the extracted one takes its name. The rename happens in the
/// commit, file by file, so an extraction that fails before it changes nothing, and a file that
/// cannot be moved in leaves the existing one under its own name. Every way an entry reaches the
/// conflict rule is covered: a plain extraction, a selection, a filter, a single file unwrapped
/// into the parent folder (T-F154), duplicate names inside the archive.
/// </summary>
public sealed class RenameExistingConflictTests : IDisposable
{
    private readonly ZipArchiveService _sut = new(new GroupPolicyOptions());
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string CreateZip(params (string Name, string Content)[] entries)
    {
        string zipPath = Path.Combine(_temp.Path, "in-" + Path.GetRandomFileName() + ".zip");
        using ZipArchive archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach ((string name, string content) in entries)
        {
            using var writer = new StreamWriter(archive.CreateEntry(name).Open());
            writer.Write(content);
        }
        return zipPath;
    }

    private string CreateDest(params (string Name, string Content)[] files)
    {
        string dest = Path.Combine(_temp.Path, "dest");
        Directory.CreateDirectory(dest);
        foreach ((string name, string content) in files)
            File.WriteAllText(Path.Combine(dest, name), content);
        return dest;
    }

    private Task<ArchiveResult> ExtractAsync(
        string zip, string dest, ExtractOptions? extra = null, ExtractMode mode = ExtractMode.SingleFolder) =>
        _sut.ExtractAsync((extra ?? new ExtractOptions()) with
        {
            ArchivePaths = [zip], DestinationFolder = dest, Mode = mode,
            OnConflict = ConflictBehavior.RenameExisting,
        });

    private static string Read(string dest, string name) => File.ReadAllText(Path.Combine(dest, name));

    [Fact]
    public async Task ExtractAsync_ExistingFile_IsRenamedAndTheNewOneTakesItsName()
    {
        string zip = CreateZip(("a.txt", "new"), ("b.txt", "b"));
        string dest = CreateDest(("a.txt", "old"));

        ArchiveResult result = await ExtractAsync(zip, dest);

        result.Errors.Should().BeEmpty();
        Read(dest, "a.txt").Should().Be("new");
        Read(dest, "a (1).txt").Should().Be("old");
        Read(dest, "b.txt").Should().Be("b");
    }

    // T-F419: 7-Zip's -aot renames a folder of the entry's name too; its content is kept.
    [Fact]
    public async Task ExtractAsync_FolderWhereTheEntryGoes_IsRenamedAndTheFileTakesItsName()
    {
        string zip = CreateZip(("a.txt", "new"));
        string dest = CreateDest();
        Directory.CreateDirectory(Path.Combine(dest, "a.txt"));
        File.WriteAllText(Path.Combine(dest, "a.txt", "inner.txt"), "inner");

        ArchiveResult result = await ExtractAsync(zip, dest);

        result.Errors.Should().BeEmpty();
        Read(dest, "a.txt").Should().Be("new");
        Read(dest, Path.Combine("a (1).txt", "inner.txt")).Should().Be("inner");
    }

    // T-F419: under Overwrite a folder in the way fails the entry with its own reason, not a lock.
    [Fact]
    public async Task ExtractAsync_Overwrite_FolderWhereTheEntryGoes_IsKeptAndNamedAsAFolder()
    {
        string zip = CreateZip(("a.txt", "new"));
        string dest = CreateDest();
        Directory.CreateDirectory(Path.Combine(dest, "a.txt"));
        File.WriteAllText(Path.Combine(dest, "a.txt", "inner.txt"), "inner");

        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [zip], DestinationFolder = dest, Mode = ExtractMode.SingleFolder, OnConflict = ConflictBehavior.Overwrite,
        });

        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.DestinationIsAFolder);
        Read(dest, Path.Combine("a.txt", "inner.txt")).Should().Be("inner");
    }

    // T-F419: an overwrite replaces a read-only file, as 7-Zip's -aoa does.
    [Fact]
    public async Task ExtractAsync_Overwrite_ReadOnlyFile_IsReplaced()
    {
        string zip = CreateZip(("a.txt", "new"));
        string dest = CreateDest(("a.txt", "old"));
        string existing = Path.Combine(dest, "a.txt");
        File.SetAttributes(existing, FileAttributes.ReadOnly);

        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [zip], DestinationFolder = dest, Mode = ExtractMode.SingleFolder, OnConflict = ConflictBehavior.Overwrite,
        });

        result.Errors.Should().BeEmpty();
        Read(dest, "a.txt").Should().Be("new");
    }

    // The renamed file never takes a name the archive is about to write.
    [Fact]
    public async Task ExtractAsync_NumberedNameComesFromTheArchive_ExistingTakesTheNextOne()
    {
        string zip = CreateZip(("a.txt", "new"), ("a (1).txt", "from archive"));
        string dest = CreateDest(("a.txt", "old"));

        ArchiveResult result = await ExtractAsync(zip, dest);

        result.Errors.Should().BeEmpty();
        Read(dest, "a.txt").Should().Be("new");
        Read(dest, "a (1).txt").Should().Be("from archive");
        Read(dest, "a (2).txt").Should().Be("old");
    }

    // "readme" is moved in before "readme (1)" (NTFS lists the shorter name first): without the
    // run's own names the old file would take "readme (1)", which the archive then overwrites.
    [Fact]
    public async Task ExtractAsync_NumberedNameStillToCome_ExistingIsNotOverwrittenByIt()
    {
        string zip = CreateZip(("readme", "new"), ("readme (1)", "from archive"));
        string dest = CreateDest(("readme", "old"));

        ArchiveResult result = await ExtractAsync(zip, dest);

        result.Errors.Should().BeEmpty();
        Read(dest, "readme").Should().Be("new");
        Read(dest, "readme (1)").Should().Be("from archive");
        Read(dest, "readme (2)").Should().Be("old");
    }

    [Fact]
    public async Task ExtractAsync_ExistingFileLocked_StaysAsItWasAndTheRestArrives()
    {
        string zip = CreateZip(("a.txt", "new"), ("b.txt", "b"));
        string dest = CreateDest(("a.txt", "old"));

        ArchiveResult result;
        using (new FileStream(Path.Combine(dest, "a.txt"), FileMode.Open, FileAccess.Read, FileShare.Read))
            result = await ExtractAsync(zip, dest);

        result.Errors.Should().ContainSingle();
        Read(dest, "a.txt").Should().Be("old");
        File.Exists(Path.Combine(dest, "a (1).txt")).Should().BeFalse();
        Read(dest, "b.txt").Should().Be("b");
    }

    // Two entries named alike: the second meets the first, which is not on disk yet - it is
    // renamed itself, as -aou would; the file that was there before is still kept.
    [Fact]
    public async Task ExtractAsync_DuplicateNamesInTheArchive_EveryCopyAndTheExistingFileAreKept()
    {
        string zip = CreateZip(("a.txt", "first"), ("a.txt", "second"));
        string dest = CreateDest(("a.txt", "old"));

        ArchiveResult result = await ExtractAsync(zip, dest);

        result.Errors.Should().BeEmpty();
        Read(dest, "a.txt").Should().Be("first");
        Read(dest, "a (1).txt").Should().Be("second");
        Read(dest, "a (2).txt").Should().Be("old");
    }

    [Fact]
    public async Task ExtractAsync_WithAFilter_ExistingFileIsRenamed()
    {
        string zip = CreateZip(("a.txt", "new"), ("b.txt", "b"));
        string dest = CreateDest(("a.txt", "old"));

        ArchiveResult result = await ExtractAsync(zip, dest, new ExtractOptions { EntryFilter = (name, _) => name == "a.txt" });

        result.Errors.Should().BeEmpty();
        Read(dest, "a.txt").Should().Be("new");
        Read(dest, "a (1).txt").Should().Be("old");
        File.Exists(Path.Combine(dest, "b.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task ExtractAsync_WithASelection_ExistingFileIsRenamed()
    {
        string zip = CreateZip(("d/a.txt", "new"), ("b.txt", "b"));
        string dest = CreateDest();
        Directory.CreateDirectory(Path.Combine(dest, "d"));
        File.WriteAllText(Path.Combine(dest, "d", "a.txt"), "old");

        ArchiveResult result = await ExtractAsync(zip, dest, new ExtractOptions { SelectedEntryPaths = ["d"] });

        result.Errors.Should().BeEmpty();
        Read(dest, Path.Combine("d", "a.txt")).Should().Be("new");
        Read(dest, Path.Combine("d", "a (1).txt")).Should().Be("old");
    }

    // T-F154: a one-file archive in SeparateFolders lands in the parent folder, not in its own.
    [Fact]
    public async Task ExtractAsync_SingleFileUnwrappedIntoTheParent_ExistingFileIsRenamed()
    {
        string zip = CreateZip(("a.txt", "new"));
        string dest = CreateDest(("a.txt", "old"));

        ArchiveResult result = await ExtractAsync(zip, dest, mode: ExtractMode.SeparateFolders);

        result.Errors.Should().BeEmpty();
        Read(dest, "a.txt").Should().Be("new");
        Read(dest, "a (1).txt").Should().Be("old");
    }

    // Stopped before the commit: the existing file was never touched.
    [Fact]
    public async Task ExtractAsync_CancelledBeforeTheCommit_ExistingFileIsUntouched()
    {
        string zip = CreateZip(("a.txt", "new"), ("b.txt", "b"));
        string dest = CreateDest(("a.txt", "old"));
        using var cts = new CancellationTokenSource();

        Func<Task> act = () => _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [zip], DestinationFolder = dest, Mode = ExtractMode.SingleFolder,
            OnConflict = ConflictBehavior.RenameExisting,
            EntryFilter = (name, _) => { if (name == "b.txt") cts.Cancel(); return true; },
        }, cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        Read(dest, "a.txt").Should().Be("old");
        Directory.GetFiles(dest).Select(Path.GetFileName).Should().Equal("a.txt");
    }

    // --- ExtractionStaging.CommitInto, directly ---

    // The new file cannot be moved in (held open without delete sharing): the existing one goes back.
    [Fact]
    public void CommitInto_NewFileCannotBeMovedIn_ExistingFileGetsItsNameBack()
    {
        string dest = CreateDest(("a.txt", "old"));
        using var staging = ExtractionStaging.Create(_temp.Path);
        string staged = Path.Combine(staging.Path, "a.txt");
        File.WriteAllText(staged, "new");
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.Combine(dest, "a.txt") };
        staging.RenameExistingOnCommit(Path.Combine(dest, "a.txt"), claimed);

        IReadOnlyList<string> locked;
        using (new FileStream(staged, FileMode.Open, FileAccess.Read, FileShare.Read))
            locked = staging.CommitInto(dest);

        locked.Should().Equal("a.txt");
        Read(dest, "a.txt").Should().Be("old");
        Directory.GetFiles(dest).Select(Path.GetFileName).Should().Equal("a.txt");
    }

    // Review finding: a folder with the numbered name is taken too, not a target to fail on.
    [Fact]
    public void CommitInto_NumberedNameIsAFolder_ExistingTakesTheNextName()
    {
        string dest = CreateDest(("a.txt", "old"));
        Directory.CreateDirectory(Path.Combine(dest, "a (1).txt"));
        using var staging = ExtractionStaging.Create(_temp.Path);
        File.WriteAllText(Path.Combine(staging.Path, "a.txt"), "new");
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.Combine(dest, "a.txt") };
        staging.RenameExistingOnCommit(Path.Combine(dest, "a.txt"), claimed);

        staging.CommitInto(dest).Should().BeEmpty();

        Read(dest, "a.txt").Should().Be("new");
        Read(dest, "a (2).txt").Should().Be("old");
    }

    // The destination as a caller may give it, not resolved: claimed names are full paths.
    [Fact]
    public void CommitInto_DestinationNotCanonical_StillAvoidsClaimedNames()
    {
        string dest = CreateDest(("readme", "old"));
        using var staging = ExtractionStaging.Create(_temp.Path);
        File.WriteAllText(Path.Combine(staging.Path, "readme"), "new");
        File.WriteAllText(Path.Combine(staging.Path, "readme (1)"), "from archive");
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { Path.Combine(dest, "readme"), Path.Combine(dest, "readme (1)") };
        staging.RenameExistingOnCommit(Path.Combine(dest, "readme"), claimed);
        string nonCanonical = dest.Replace('\\', '/') + "/./";

        staging.CommitInto(nonCanonical).Should().BeEmpty();

        Read(dest, "readme").Should().Be("new");
        Read(dest, "readme (1)").Should().Be("from archive");
        Read(dest, "readme (2)").Should().Be("old");
    }

    [Fact]
    public void CommitInto_RenameExistingWithNothingThere_MovesTheNewFileIn()
    {
        string dest = CreateDest(("other.txt", "x"));
        using var staging = ExtractionStaging.Create(_temp.Path);
        File.WriteAllText(Path.Combine(staging.Path, "a.txt"), "new");
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.Combine(dest, "a.txt") };
        staging.RenameExistingOnCommit(Path.Combine(dest, "a.txt"), claimed);

        staging.CommitInto(dest).Should().BeEmpty();

        Read(dest, "a.txt").Should().Be("new");
        Directory.GetFiles(dest).Should().HaveCount(2);
    }
}
