using System.Text;
using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// T-F298: tar-family extraction keeps the archive's times through the real sandboxed tar.exe —
/// files (tar.exe sets them; writing the Zone.Identifier stream used to reset them) and folder
/// entries (moving files into a folder changes its time, so it is set after the commit).
/// </summary>
[Collection("TarSandbox")]
public sealed class TarSandboxedServiceExtractTimesTests : IDisposable
{
    private static readonly DateTime FileUtc = new(2019, 3, 4, 10, 20, 30, DateTimeKind.Utc);
    private static readonly DateTime FolderUtc = new(2017, 8, 9, 11, 12, 14, DateTimeKind.Utc);

    private readonly TarSandboxedService _sut = new(new GroupPolicyOptions());
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static long Unix(DateTime utc) => new DateTimeOffset(utc).ToUnixTimeSeconds();

    private static TarBuilder.Entry File(string name, string content = "data") =>
        new() { Name = name, Content = Encoding.ASCII.GetBytes(content), ModifiedUnixSeconds = Unix(FileUtc) };

    private static TarBuilder.Entry Folder(string name) =>
        new() { Name = name, TypeFlag = '5', ModifiedUnixSeconds = Unix(FolderUtc) };

    private string Tar(string name, params TarBuilder.Entry[] entries)
    {
        string path = Path.Combine(_temp.Path, name);
        TarBuilder.WriteTar(path, entries);
        return path;
    }

    private async Task<string> ExtractAsync(string archive, ExtractMode mode = ExtractMode.SingleFolder, string? destDir = null)
    {
        destDir ??= Path.Combine(_temp.Path, "out-" + Path.GetRandomFileName());
        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archive],
            DestinationFolder = destDir,
            Mode = mode,
        });
        result.Success.Should().BeTrue(because: string.Join("; ", result.Errors.Select(e => e.Message)));
        return destDir;
    }

    [Integration]
    public async Task ExtractAsync_ArchiveWithZoneIdentifier_FileKeepsMotwAndItsTime()
    {
        string archive = Tar("motw.tar", File("a.txt"), File("b.txt"));
        System.IO.File.WriteAllText(archive + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");

        string dest = await ExtractAsync(archive);

        string file = Path.Combine(dest, "a.txt");
        System.IO.File.ReadAllText(file + ":Zone.Identifier").Should().Contain("ZoneId=3");
        System.IO.File.GetLastWriteTimeUtc(file).Should().Be(FileUtc);
    }

    [Integration]
    public async Task ExtractAsync_FolderEntry_NewDestination_FolderGetsItsTime()
    {
        string archive = Tar("folders.tar", Folder("a/"), File("a/f.txt"), File("b.txt"));

        string dest = await ExtractAsync(archive);

        Directory.GetLastWriteTimeUtc(Path.Combine(dest, "a")).Should().Be(FolderUtc);
        System.IO.File.GetLastWriteTimeUtc(Path.Combine(dest, "a", "f.txt")).Should().Be(FileUtc);
    }

    [Integration]
    public async Task ExtractAsync_FolderEntry_MergeIntoExistingDestination_NewFolderGetsItsTimeExistingOneIsKept()
    {
        string dest = Path.Combine(_temp.Path, "existing");
        Directory.CreateDirectory(Path.Combine(dest, "old"));
        DateTime oldFolderTime = new(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Directory.SetLastWriteTimeUtc(Path.Combine(dest, "old"), oldFolderTime);
        string archive = Tar("merge.tar", Folder("a/"), File("a/f.txt"), Folder("old/"), File("old/g.txt"));

        await ExtractAsync(archive, destDir: dest);

        Directory.GetLastWriteTimeUtc(Path.Combine(dest, "a")).Should().Be(FolderUtc);
        Directory.GetLastWriteTimeUtc(Path.Combine(dest, "old")).Should().NotBe(FolderUtc);
    }

    // T-F52 guard: a file ahead of its own folder's entry still needs Pakko to create that folder
    // first; and an implicit subfolder under an explicit one still extracts.
    [Integration]
    public async Task ExtractAsync_FileBeforeItsFolderEntryAndImplicitSubfolder_StillExtract()
    {
        string archive = Tar("order.tar", File("a/f.txt"), Folder("a/"), Folder("b/"), File("b/c/d.txt"), File("e.txt"));

        string dest = await ExtractAsync(archive);

        System.IO.File.Exists(Path.Combine(dest, "a", "f.txt")).Should().BeTrue();
        System.IO.File.Exists(Path.Combine(dest, "b", "c", "d.txt")).Should().BeTrue();
        System.IO.File.GetLastWriteTimeUtc(Path.Combine(dest, "b", "c", "d.txt")).Should().Be(FileUtc);
    }

    [Integration]
    public async Task ExtractAsync_SeparateFolders_SingleRootFolderEntry_TheCreatedFolderGetsTheRootTime()
    {
        string archive = Tar("rooted.tar", Folder("root/"), File("root/f.txt"));

        string dest = await ExtractAsync(archive, ExtractMode.SeparateFolders);

        Directory.GetLastWriteTimeUtc(Path.Combine(dest, "rooted")).Should().Be(FolderUtc);
        System.IO.File.GetLastWriteTimeUtc(Path.Combine(dest, "rooted", "f.txt")).Should().Be(FileUtc);
    }
}
