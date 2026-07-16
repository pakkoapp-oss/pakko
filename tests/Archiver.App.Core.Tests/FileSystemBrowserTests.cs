using FluentAssertions;

namespace Archiver.App.Core.Tests;

public sealed class FileSystemBrowserTests : IDisposable
{
    private readonly string _root;

    public FileSystemBrowserTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "PakkoFileSystemBrowserTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void ListFolder_MixedFoldersAndFiles_SortsFoldersFirstThenAlphabetical()
    {
        Directory.CreateDirectory(Path.Combine(_root, "zzz_folder"));
        Directory.CreateDirectory(Path.Combine(_root, "aaa_folder"));
        File.WriteAllText(Path.Combine(_root, "zebra.txt"), "z");
        File.WriteAllText(Path.Combine(_root, "apple.txt"), "a");

        var entries = FileSystemBrowser.ListFolder(_root);

        entries.Select(e => e.Name).Should().ContainInOrder("aaa_folder", "zzz_folder", "apple.txt", "zebra.txt");
    }

    [Fact]
    public void ListFolder_File_PopulatesSizeAndModifiedButNotCompressedSizeOrCrc()
    {
        string filePath = Path.Combine(_root, "data.bin");
        File.WriteAllBytes(filePath, new byte[1234]);

        var entries = FileSystemBrowser.ListFolder(_root);

        var file = entries.Single(e => e.Name == "data.bin");
        file.IsFolder.Should().BeFalse();
        file.Size.Should().Be(1234);
        file.CompressedSize.Should().Be(0);
        file.Crc32.Should().BeNull();
        file.Modified.Should().NotBeNull();
        file.FullPath.Should().Be(filePath);
    }

    [Fact]
    public void ListFolder_Folder_IsFolderTrueAndSizeDisplayEmpty()
    {
        Directory.CreateDirectory(Path.Combine(_root, "subfolder"));

        var entries = FileSystemBrowser.ListFolder(_root);

        var folder = entries.Single(e => e.Name == "subfolder");
        folder.IsFolder.Should().BeTrue();
        folder.SizeDisplay.Should().BeEmpty();
    }

    [Fact]
    public void ListFolder_NonexistentPath_ReturnsEmptyRatherThanThrowing()
    {
        string missing = Path.Combine(_root, "does_not_exist");

        var entries = FileSystemBrowser.ListFolder(missing);

        entries.Should().BeEmpty();
    }

    [Fact]
    public void ListDrives_ReturnsAtLeastOneReadyDriveAsAFolder()
    {
        var drives = FileSystemBrowser.ListDrives();

        drives.Should().NotBeEmpty();
        drives.Should().OnlyContain(d => d.IsFolder);
    }
}
