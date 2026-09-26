using FluentAssertions;

namespace Archiver.App.Core.Tests;

// T-F232: the constructor threw on a path it could not read, so one bad path in an activation list
// dropped the whole list, or escaped into MainWindow's drag-drop handler.
public sealed class FileItemTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "FileItemTests_" + Guid.NewGuid().ToString("N"));

    public FileItemTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public async Task TryCreate_ExistingFile_ReturnsItemWithSizeAndCrc()
    {
        var path = Path.Combine(_dir, "файл.txt");
        File.WriteAllText(path, "12345");

        var item = FileItem.TryCreate(path);

        item.Should().NotBeNull();
        item!.FullPath.Should().Be(path);
        item.SizeBytes.Should().Be(5);
        item.Type.Should().Be("TXT");

        // The CRC read runs in the background with the file open; wait for it so Dispose can delete.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (item.Crc32 is null && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        item.Crc32Display.Should().Be("CBF53A1C");
    }

    [Fact]
    public void TryCreate_ExistingFolder_ReturnsFolderItem()
    {
        var item = FileItem.TryCreate(_dir);

        item.Should().NotBeNull();
        item!.Type.Should().Be("Folder");
    }

    [Fact]
    public void TryCreate_MissingPath_ReturnsNull()
    {
        FileItem.TryCreate(Path.Combine(_dir, "missing.zip")).Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("C:\\bad\0name.zip")]
    [InlineData("C:\\a:b:c.zip")]
    public void TryCreate_InvalidPath_ReturnsNull(string path)
    {
        FileItem.TryCreate(path).Should().BeNull();
    }
}
