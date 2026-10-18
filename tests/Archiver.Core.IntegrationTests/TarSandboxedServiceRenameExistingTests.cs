using System.Text;
using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// T-F411: <see cref="ConflictBehavior.RenameExisting"/> (7-Zip's -aot) through tar.exe - the same
/// commit as ZIP's (ExtractionStaging), reached from the tar engine's own conflict rule.
/// </summary>
[Collection("TarSandbox")]
public sealed class TarSandboxedServiceRenameExistingTests : IDisposable
{
    private readonly TarSandboxedService _sut = new(new GroupPolicyOptions());
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string WriteTar(params (string Name, string Content)[] entries)
    {
        string archivePath = Path.Combine(_temp.Path, "in-" + Path.GetRandomFileName() + ".tar");
        TarBuilder.WriteTar(archivePath,
            entries.Select(e => new TarBuilder.Entry { Name = e.Name, Content = Encoding.ASCII.GetBytes(e.Content) }));
        return archivePath;
    }

    private string CreateDest(params (string Name, string Content)[] files)
    {
        string dest = Path.Combine(_temp.Path, "dest");
        Directory.CreateDirectory(dest);
        foreach ((string name, string content) in files)
            File.WriteAllText(Path.Combine(dest, name), content);
        return dest;
    }

    private Task<ArchiveResult> ExtractAsync(string archive, string dest) =>
        _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archive], DestinationFolder = dest, Mode = ExtractMode.SingleFolder,
            OnConflict = ConflictBehavior.RenameExisting,
        });

    private static string Read(string dest, string name) => File.ReadAllText(Path.Combine(dest, name));

    [Integration]
    public async Task ExtractAsync_ExistingFile_IsRenamedAndTheNewOneTakesItsName()
    {
        string tar = WriteTar(("a.txt", "new"), ("b.txt", "b"));
        string dest = CreateDest(("a.txt", "old"));

        ArchiveResult result = await ExtractAsync(tar, dest);

        result.Errors.Should().BeEmpty();
        Read(dest, "a.txt").Should().Be("new");
        Read(dest, "a (1).txt").Should().Be("old");
        Read(dest, "b.txt").Should().Be("b");
    }

    [Integration]
    public async Task ExtractAsync_ExistingFileLocked_StaysAsItWas()
    {
        string tar = WriteTar(("a.txt", "new"), ("b.txt", "b"));
        string dest = CreateDest(("a.txt", "old"));

        ArchiveResult result;
        using (new FileStream(Path.Combine(dest, "a.txt"), FileMode.Open, FileAccess.Read, FileShare.Read))
            result = await ExtractAsync(tar, dest);

        result.Errors.Should().ContainSingle();
        Read(dest, "a.txt").Should().Be("old");
        File.Exists(Path.Combine(dest, "a (1).txt")).Should().BeFalse();
        Read(dest, "b.txt").Should().Be("b");
    }

    [Integration]
    public async Task ExtractAsync_DuplicateNamesInTheArchive_EveryCopyAndTheExistingFileAreKept()
    {
        string tar = WriteTar(("a.txt", "first"), ("a.txt", "second"));
        string dest = CreateDest(("a.txt", "old"));

        ArchiveResult result = await ExtractAsync(tar, dest);

        result.Errors.Should().BeEmpty();
        Directory.GetFiles(dest).Select(f => File.ReadAllText(f)).Should().BeEquivalentTo(["first", "second", "old"]);
        Read(dest, "a (2).txt").Should().Be("old");
    }
}
