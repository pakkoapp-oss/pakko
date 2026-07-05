using System.Runtime.InteropServices;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;
using Microsoft.Win32.SafeHandles;

namespace Archiver.Core.Tests.Services;

/// <summary>
/// T-F20: Zip64 verification. .NET's System.IO.Compression already implements Zip64 (the
/// central-directory/local-header extension for entry counts &gt;65535 or sizes &gt;4 GiB) — these
/// tests confirm ZipArchiveService's own code paths (progress tracking, ZIP-bomb ratio check,
/// smart foldering) don't break on the boundary, not that Zip64 itself works.
///
/// All tests here are tagged [Trait("Category", "Slow")] and excluded from the default
/// `dotnet test` run — measured at ~30s each for the &gt;65535-file cases (real NTFS per-file
/// creation/traversal overhead, not something a code change here can speed up) and multi-GB
/// disk I/O for the &gt;4 GiB case. Run explicitly with
/// `dotnet test --filter "Category=Slow"` (see CLAUDE.md/TESTING.md).
/// </summary>
public sealed class ZipArchiveServiceZip64Tests : IDisposable
{
    private readonly ZipArchiveService _sut = new();
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    [Trait("Category", "Slow")]
    public async Task ArchiveAsync_MoreThan65535Files_CompletesWithoutError()
    {
        const int fileCount = 65_600; // just over the 16-bit ZIP entry-count limit
        string sourceDir = Path.Combine(_temp.Path, "many_files");
        Directory.CreateDirectory(sourceDir);

        for (int i = 0; i < fileCount; i++)
            File.Create(Path.Combine(sourceDir, $"f{i}.txt")).Dispose(); // 0-byte — fast to create

        var result = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [sourceDir],
            DestinationFolder = _temp.Path,
            ArchiveName = "many_files_archive"
        });

        result.Success.Should().BeTrue();
        result.Errors.Should().BeEmpty();

        using var zip = System.IO.Compression.ZipFile.OpenRead(result.CreatedFiles[0]);
        zip.Entries.Should().HaveCount(fileCount);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task ExtractAsync_ArchiveWithMoreThan65535Entries_ExtractsAllFiles()
    {
        const int fileCount = 65_600;
        string sourceDir = Path.Combine(_temp.Path, "many_files_src");
        Directory.CreateDirectory(sourceDir);
        for (int i = 0; i < fileCount; i++)
            File.Create(Path.Combine(sourceDir, $"f{i}.txt")).Dispose();

        var archiveResult = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [sourceDir],
            DestinationFolder = _temp.Path,
            ArchiveName = "many_files_extract"
        });
        archiveResult.Success.Should().BeTrue();

        string extractDest = Path.Combine(_temp.Path, "many_files_out");
        var extractResult = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archiveResult.CreatedFiles[0]],
            DestinationFolder = extractDest,
            Mode = ExtractMode.SingleFolder
        });

        extractResult.Success.Should().BeTrue();
        extractResult.Errors.Should().BeEmpty();
        Directory.GetFiles(extractDest).Should().HaveCount(fileCount);
    }

    // Marks a freshly-created file as an NTFS sparse file (FSCTL_SET_SPARSE) so a >4 GiB length
    // can be set without allocating real disk blocks for the all-zero content — the filesystem
    // returns zeros for unallocated ranges on read, so archiving/extracting it exercises Zip64's
    // large-size code paths without multi-minute disk I/O. Not supported on non-NTFS volumes
    // (e.g. FAT32 USB drives, some CI runners) — callers must handle SetSparse returning false.
    private static bool TrySetSparse(SafeFileHandle handle)
    {
        const uint FSCTL_SET_SPARSE = 0x000900C4;
        return DeviceIoControl(handle, FSCTL_SET_SPARSE, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice, uint dwIoControlCode,
        IntPtr lpInBuffer, uint nInBufferSize,
        IntPtr lpOutBuffer, uint nOutBufferSize,
        out uint lpBytesReturned, IntPtr lpOverlapped);

    private static string? CreateSparseFileOver4Gb(string path)
    {
        const long overFourGiB = 4L * 1024 * 1024 * 1024 + 4096;
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        if (!TrySetSparse(fs.SafeFileHandle))
            return null; // volume doesn't support sparse files — skip gracefully
        fs.SetLength(overFourGiB);
        return path;
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task ArchiveAndExtract_FileOver4Gb_RoundTripsWithoutError()
    {
        string bigFile = Path.Combine(_temp.Path, "big.bin");
        if (CreateSparseFileOver4Gb(bigFile) is null)
            return; // sparse files not supported on this volume — skip gracefully

        var archiveResult = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [bigFile],
            DestinationFolder = _temp.Path,
            ArchiveName = "big_archive",
            // Stored, not Deflate: an all-zero source compresses at a ratio our own ZIP-bomb
            // check (MaxCompressionRatio = 1000) would reject on extract, and Stored avoids
            // spending CPU time compressing 4 GiB of zeros just to prove Zip64 itself works.
            CompressionLevel = System.IO.Compression.CompressionLevel.NoCompression
        });

        archiveResult.Success.Should().BeTrue();
        archiveResult.Errors.Should().BeEmpty();

        string extractDest = Path.Combine(_temp.Path, "big_out");
        var extractResult = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archiveResult.CreatedFiles[0]],
            DestinationFolder = extractDest,
            Mode = ExtractMode.SingleFolder
        });

        extractResult.Success.Should().BeTrue();
        extractResult.Errors.Should().BeEmpty();

        string extractedFile = Path.Combine(extractDest, "big.bin");
        File.Exists(extractedFile).Should().BeTrue();
        new FileInfo(extractedFile).Length.Should().Be(new FileInfo(bigFile).Length);
    }
}
