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

        IReadOnlyList<ArchiveEntryViewModel> entries = FileSystemBrowser.ListFolder(_root);

        entries.Select(e => e.Name).Should().ContainInOrder("aaa_folder", "zzz_folder", "apple.txt", "zebra.txt");
    }

    [Fact]
    public void ListFolder_File_PopulatesSizeAndModifiedButNotCompressedSizeOrCrc()
    {
        string filePath = Path.Combine(_root, "data.bin");
        File.WriteAllBytes(filePath, new byte[1234]);

        IReadOnlyList<ArchiveEntryViewModel> entries = FileSystemBrowser.ListFolder(_root);

        ArchiveEntryViewModel file = entries.Single(e => e.Name == "data.bin");
        file.IsFolder.Should().BeFalse();
        file.Size.Should().Be(1234);
        file.CompressedSize.Should().BeNull();
        file.Crc32.Should().BeNull();
        file.Modified.Should().NotBeNull();
        file.FullPath.Should().Be(filePath);
    }

    [Fact]
    public void ListFolder_Folder_IsFolderTrueAndSizeDisplayEmpty()
    {
        Directory.CreateDirectory(Path.Combine(_root, "subfolder"));

        IReadOnlyList<ArchiveEntryViewModel> entries = FileSystemBrowser.ListFolder(_root);

        ArchiveEntryViewModel folder = entries.Single(e => e.Name == "subfolder");
        folder.IsFolder.Should().BeTrue();
        folder.SizeDisplay.Should().BeEmpty();
    }

    [Fact]
    public void ListFolder_NonexistentPath_ReturnsEmptyRatherThanThrowing()
    {
        string missing = Path.Combine(_root, "does_not_exist");

        IReadOnlyList<ArchiveEntryViewModel> entries = FileSystemBrowser.ListFolder(missing);

        entries.Should().BeEmpty();
    }

    // T-F324: what Explorer hides as "protected operating system files" ($Recycle.Bin,
    // pagefile.sys). A Hidden-only folder stays: Up from an archive under %TEMP% climbs through
    // AppData, which is Hidden only.
    [Fact]
    public void ListFolder_HiddenAndSystemEntries_AreLeftOut()
    {
        string folder = Path.Combine(_root, "protected_folder");
        Directory.CreateDirectory(folder);
        File.SetAttributes(folder, FileAttributes.Directory | FileAttributes.Hidden | FileAttributes.System);
        string file = Path.Combine(_root, "protected.sys");
        File.WriteAllText(file, "x");
        File.SetAttributes(file, FileAttributes.Hidden | FileAttributes.System);
        File.WriteAllText(Path.Combine(_root, "plain.txt"), "x");

        IReadOnlyList<ArchiveEntryViewModel> entries = FileSystemBrowser.ListFolder(_root);

        entries.Select(e => e.Name).Should().Equal("plain.txt");
    }

    [Theory]
    [InlineData(FileAttributes.Hidden)]
    [InlineData(FileAttributes.System)]
    public void ListFolder_HiddenOnlyOrSystemOnlyEntries_AreListed(FileAttributes attribute)
    {
        string folder = Path.Combine(_root, "folder");
        Directory.CreateDirectory(folder);
        File.SetAttributes(folder, FileAttributes.Directory | attribute);
        string file = Path.Combine(_root, "file.txt");
        File.WriteAllText(file, "x");
        File.SetAttributes(file, attribute);

        IReadOnlyList<ArchiveEntryViewModel> entries = FileSystemBrowser.ListFolder(_root);

        entries.Select(e => e.Name).Should().Equal("folder", "file.txt");
    }

    [Fact]
    public void ListDrives_ReturnsAtLeastOneReadyDriveAsAFolder()
    {
        IReadOnlyList<ArchiveEntryViewModel> drives = FileSystemBrowser.ListDrives();

        drives.Should().NotBeEmpty();
        drives.Should().OnlyContain(d => d.IsFolder);
    }
}
