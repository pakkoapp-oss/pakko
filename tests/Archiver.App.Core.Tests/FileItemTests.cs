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
        string path = Path.Combine(_dir, "файл.txt");
        File.WriteAllText(path, "12345");

        var item = FileItem.TryCreate(path);

        item.Should().NotBeNull();
        item!.FullPath.Should().Be(path);
        item.SizeBytes.Should().Be(5);
        item.Type.Should().Be("TXT");

        // T-F311: Crc32 is set before Crc32Display, so a wait on Crc32 could read the display too early.
        await item.Crc32Ready;
        item.Crc32Display.Should().Be("CBF53A1C");
    }

    [Fact]
    public void TryCreate_ExistingFolder_ReturnsFolderItem()
    {
        var item = FileItem.TryCreate(_dir);

        item.Should().NotBeNull();
        item!.Type.Should().Be("Folder");
    }

    // T-F198 item 2: a screen reader reads a list row by its item's ToString().
    [Fact]
    public void ToString_IsTheName()
    {
        FileItem.TryCreate(_dir)!.ToString().Should().Be(Path.GetFileName(_dir));
    }

    // T-F236: the folder's size and file count come from the engines' walk; the archive command sums
    // them instead of walking every folder again on the UI thread.
    [Fact]
    public async Task TotalsReady_Folder_GivesBytesAndFileCount_LinkNotFollowed()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "sub"));
        File.WriteAllBytes(Path.Combine(_dir, "a.bin"), new byte[10]);
        File.WriteAllBytes(Path.Combine(_dir, "sub", "b.bin"), new byte[20]);
        string outside = _dir + "_outside";
        Directory.CreateDirectory(outside);
        File.WriteAllBytes(Path.Combine(outside, "big.bin"), new byte[1000]);
        Archiver.Core.Services.DirectoryJunction.Create(Path.Combine(_dir, "link"), outside);
        try
        {
            using FileItem item = FileItem.TryCreate(_dir)!;
            await item.TotalsReady;

            item.SizeBytes.Should().Be(30);
            item.FileCount.Should().Be(2);
        }
        finally
        {
            Directory.Delete(Path.Combine(_dir, "link"), recursive: false);
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public async Task TotalsReady_File_IsOneFile()
    {
        string path = Path.Combine(_dir, "a.bin");
        File.WriteAllBytes(path, new byte[7]);

        using FileItem item = FileItem.TryCreate(path)!;
        await item.TotalsReady;

        item.SizeBytes.Should().Be(7);
        item.FileCount.Should().Be(1);
        await item.Crc32Ready;
    }

    // Removing a row (or Clear) disposes its item: a walk of a whole drive used to run on after the
    // row was gone. A disposed item's totals still complete, never fault.
    [Fact]
    public async Task Dispose_StopsTheFolderWalk_TotalsCompleteWithoutFault()
    {
        for (int i = 0; i < 300; i++)
        {
            string sub = Path.Combine(_dir, $"d{i}");
            Directory.CreateDirectory(sub);
            File.WriteAllBytes(Path.Combine(sub, "f.bin"), new byte[1]);
        }

        FileItem item = FileItem.TryCreate(_dir)!;
        item.Dispose();
        Func<Task> wait = () => item.TotalsReady.WaitAsync(TimeSpan.FromSeconds(10));

        await wait.Should().NotThrowAsync();
        item.FileCount.Should().BeLessThan(300, "the walk stopped before it finished");
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
