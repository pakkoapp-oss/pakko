using System.IO.Compression;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;
using Microsoft.Win32;

namespace Archiver.Core.Tests.Services;

// T-F243 item 6, end to end: a real file whose archive entry name is over 65,535 UTF-8 bytes
// (90 nested 250-character CJK folders). Needs Windows long paths (LongPathsEnabled).
public sealed class ZipArchiveServiceLongEntryNameTests : IDisposable
{
    private readonly ZipArchiveService _sut = new();
    private readonly TempDirectory _temp = new();

    public void Dispose()
    {
        try { Directory.Delete(_temp.Path, recursive: true); } catch { /* best-effort: TempDirectory retries */ }
        _temp.Dispose();
    }

    private static bool LongPathsEnabled() =>
        OperatingSystem.IsWindows()
        && Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\FileSystem", "LongPathsEnabled", 0) is 1;

    private string CreateSource(int extraFiles)
    {
        string root = Path.Combine(_temp.Path, "src");
        string deep = root;
        for (int i = 0; i < 90; i++)
            deep = Path.Combine(deep, new string((char)(0x4E00 + i), 250));
        Directory.CreateDirectory(deep);
        File.WriteAllText(Path.Combine(deep, "deep.txt"), "deep");
        for (int i = 0; i < extraFiles; i++)
            File.WriteAllText(Path.Combine(root, $"f{i}.txt"), i.ToString());
        return root;
    }

    [Theory]
    [InlineData(2)]   // sequential ZipArchive path
    [InlineData(80)]  // parallel ZipEntryWriter path
    public async Task ArchiveAsync_EntryNameOverLimit_IsOneErrorOthersArchived(int extraFiles)
    {
        if (!LongPathsEnabled())
            return;
        string src = CreateSource(extraFiles);

        var result = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [src],
            DestinationFolder = _temp.Path,
            ArchiveName = "out",
            Mode = ArchiveMode.SingleArchive,
        });

        result.Errors.Should().ContainSingle().Which.Message.Should().Contain("too long");
        using var archive = ZipFile.OpenRead(Path.Combine(_temp.Path, "out.zip"));
        archive.Entries.Count(e => e.Name.StartsWith('f')).Should().Be(extraFiles);
        archive.Entries.Should().NotContain(e => e.Name == "deep.txt");
    }
}
